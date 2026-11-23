using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SchoolPiBoard.LicenseServer.Data;
using SchoolPiBoard.LicenseServer.Endpoints;

namespace SchoolPiBoard.LicenseServer.Services;

public sealed class RenewalNotices(LicenseDbContext db, PurchaseService purchases, IEmailSender mail,
    RobokassaService gateway, IHttpClientFactory clients, IConfiguration config, ILogger<RenewalNotices> log)
{
    private string ManageUrl(bool board) => board
        ? config["Board:ManageUrl"] ?? "https://board.school-pi.online/plan"
        : config["Tariffs:ManageUrl"] ?? "https://school-pi.online/billing/my";

    private static string Date(DateTime value) => value.ToString("dd.MM.yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public async Task<bool> AdvanceAsync(TariffRecurringRequest request, CancellationToken ct)
    {
        var parent = await purchases.FindByInvoiceAsync(request.PreviousInvoiceId, ct);
        if (parent == null || parent.Kind != Payment.KindTariff || parent.Status != Payment.StatusPaid
            || parent.TariffUserId != request.UserId || string.IsNullOrWhiteSpace(parent.Email))
            return false;

        await using var tx = await purchases.LockDeliveryAsync(parent, ct);
        var end = request.EndsAt!.Value.UtcDateTime;
        if (parent.AdvanceNoticeEnd == end) return true;
        if (end <= DateTime.UtcNow.AddDays(1) || end > DateTime.UtcNow.AddDays(3)) return false;
        var message = $"Тариф «{request.PlanName}» будет продлён на {request.Days} дн. за {request.Amount:0.00} ₽. " +
            $"Первая попытка списания ожидается около {Date(end.AddDays(-1))}. " +
            $"Управлять подпиской:";
        if (!await mail.SendNoticeAsync(parent.Email, "Школа Пи — предстоящее продление тарифа", message, ManageUrl(false), ct))
            return false;
        parent.AdvanceNoticeEnd = end;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task CheckAsync(long invoice, CancellationToken ct)
    {
        var payment = await purchases.FindByInvoiceAsync(invoice, ct);
        if (payment?.RenewalEnd is not DateTime end || payment.PreviousInvoiceId is not long parentId) return;
        // Переносим повтор до сетевого запроса, чтобы недоступный магазин не задерживал очередь.
        var now = DateTime.UtcNow;
        var next = now - payment.CreatedAt < TimeSpan.FromDays(2) ? now.AddMinutes(15) : now.AddDays(1);
        var claimed = await db.Payments.Where(p => p.Id == payment.Id && p.Status == Payment.StatusPending
                && p.NextStatusCheck <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.NextStatusCheck, next), ct);
        if (claimed == 0) return;

        var reason = payment.DeclineReason;
        if (reason == null)
        {
            var shop = gateway.ShopFor(payment.Kind);
            if (shop.IsTest)
            {
                await StopCheckingAsync(invoice, ct); // OpStateExt не поддерживает тестовые платежи.
                return;
            }
            if (!shop.IsConfigured) return;
            var signature = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{shop.MerchantLogin}:{invoice}:{shop.Password2}"))));
            var endpoint = config["Robokassa:StatusUrl"] ?? "https://auth.robokassa.ru/Merchant/WebService/Service.asmx/OpStateExt";
            var url = FormattableString.Invariant($"{endpoint}?MerchantLogin={Uri.EscapeDataString(shop.MerchantLogin)}&InvoiceID={invoice}&Signature={signature}");
            using var response = await clients.CreateClient("operation-state").GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return;
            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = xml.Root;
            if (root?.Name.LocalName != "OperationStateResponse") return;
            var ns = root.Name.Namespace;
            if ((string?)root.Element(ns + "Result")?.Element(ns + "Code") != "0") return;
            var state = (string?)root.Element(ns + "State")?.Element(ns + "Code");
            reason = state switch
            {
                "10" => "операция отменена, оплата не подтверждена (статус 10)",
                "60" => "в зачислении отказано, деньги возвращены (статус 60)",
                _ => null
            };
            if (reason == null)
            {
                if (state == "100") await StopCheckingAsync(invoice, ct);
                return;
            }
        }

        var parent = await purchases.FindByInvoiceAsync(parentId, ct);
        if (parent == null) return;
        await using var tx = await purchases.LockDeliveryAsync(parent, ct);
        await db.Entry(payment).ReloadAsync(ct);
        var alreadyPaid = await db.Payments.AnyAsync(p => p.PreviousInvoiceId == parentId
            && p.RenewalEnd == end && p.Status == Payment.StatusPaid, ct);
        if (!alreadyPaid && parent.FailureNoticeEnd != end && payment.Status == Payment.StatusPending)
        {
            var board = payment.Kind == Payment.KindSubscription;
            var product = board ? "Онлайн-доска" : "Школа Пи";
            var retry = payment.RetryEligible && end > DateTime.UtcNow
                ? "При включённом автопродлении возможна повторная попытка на очередной часовой проверке до окончания срока."
                : "По этому счёту повторного списания не будет. Продление доступно в личном кабинете.";
            var text = $"{payment.Description}. Счёт {invoice}, сумма {payment.Amount:0.00} ₽. Причина: {reason}. " +
                $"Окончание оплаченного срока: {Date(end)}. {retry} Управлять подпиской:";
            log.LogWarning("Счёт {Invoice}: {Reason}", invoice, reason);
            if (string.IsNullOrWhiteSpace(payment.Email) || !await mail.SendNoticeAsync(payment.Email,
                product + " — автопродление не прошло", text, ManageUrl(board), ct)) return;
            parent.FailureNoticeEnd = end;
        }
        payment.NextStatusCheck = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private Task StopCheckingAsync(long invoice, CancellationToken ct) =>
        db.Payments.Where(p => p.InvoiceId == invoice).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.NextStatusCheck, (DateTime?)null), ct);
}

public sealed class RenewalStatusWorker(IServiceScopeFactory scopes, ILogger<RenewalStatusWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
                var due = await db.Payments.Where(p => p.Status == Payment.StatusPending && p.NextStatusCheck <= DateTime.UtcNow)
                    .OrderBy(p => p.NextStatusCheck).Take(100).Select(p => p.InvoiceId).ToListAsync(ct);
                foreach (var invoice in due)
                {
                    try
                    {
                        using var item = scopes.CreateScope();
                        await item.ServiceProvider.GetRequiredService<RenewalNotices>().CheckAsync(invoice, ct);
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        log.LogWarning("Счёт {Invoice}: ошибка проверки статуса ({Type}).", invoice, e.GetType().Name);
                    }
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested) { log.LogError(e, "Ошибка очереди уведомлений."); }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
