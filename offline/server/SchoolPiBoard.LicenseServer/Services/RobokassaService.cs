using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SchoolPiBoard.LicenseServer.Configuration;
using SchoolPiBoard.LicenseServer.Data;

namespace SchoolPiBoard.LicenseServer.Services;

/// <summary>
/// Ссылка на оплату и проверка ответа Робокассы.
///
/// Схема простая: мы отправляем покупателя на форму Робокассы с подписью,
/// посчитанной на Password1, а Робокасса вызывает наш ResultURL с подписью
/// на Password2. Карту и деньги мы не видим вообще.
/// </summary>
public sealed class RobokassaService
{
    // Кириллицу в составе чека оставляем как есть. По умолчанию
    // System.Text.Json заменяет её на \uXXXX: формально это тот же JSON,
    // но в примерах Робокассы имя товара — обычный текст, и лишнее отличие
    // при разборе ошибок только мешает.
    private static readonly JsonSerializerOptions ReceiptJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly RobokassaOptions _options;

    /// <summary>
    /// Магазин подписок — отдельный от магазина лицензий, и запасного
    /// варианта у него нет. Подписка и лицензия — разные товары с разными
    /// чеками, офертой и сайтом; провести подписку через кассу лицензий
    /// значило бы выбить покупателю чек не за то, что он купил.
    ///
    /// Не настроен — подписки не продаются вовсе.
    /// </summary>
    private readonly RobokassaOptions _board;

    public RobokassaService(ServerOptions server)
    {
        _options = server.Robokassa;

        _board = server.RobokassaBoard;
    }

    public decimal Amount => _options.Amount;

    /// <summary>
    /// Можно ли продавать подписки. Проверяется до обращения к Робокассе:
    /// не настроен свой магазин — покупателю честное «оплата пока
    /// недоступна», а не счёт из чужой кассы.
    /// </summary>
    public bool CanSellSubscriptions => _board.IsConfigured;

    /// <summary>
    /// Каким магазином выставлен счёт. Уведомление об оплате приходит на
    /// один ResultURL от обоих, а подпись считается паролем того магазина,
    /// который счёт и выставил.
    /// </summary>
    public RobokassaOptions ShopFor(string? kind)
        => kind == Payment.KindSubscription ? _board : _options;

    /// <summary>
    /// Ссылка на оплату лицензии. Сумма и назначение — из настроек: у
    /// лицензии они одни на всех.
    /// </summary>
    public string BuildPaymentUrl(long invoiceId, string email)
        => BuildPaymentUrl(_options, invoiceId, email, _options.Amount, _options.Description, recurring: false);

    /// <summary>
    /// Ссылка на оплату с собственной суммой и назначением — этим платят
    /// подписку на онлайн-доску, где цена зависит от тарифа и срока.
    ///
    /// <paramref name="recurring"/> помечает счёт как разрешающий повторные
    /// списания. Робокасса разрешает их только по такому счёту, и решить
    /// это можно лишь один раз — при первой оплате.
    /// </summary>
    public string BuildPaymentUrl(
        long invoiceId, string email, decimal amount, string description, bool recurring)
        => BuildPaymentUrl(_board, invoiceId, email, amount, description, recurring);

    private static string BuildPaymentUrl(
        RobokassaOptions shop, long invoiceId, string email, decimal amount, string description, bool recurring)
    {
        var sum = FormatSum(amount);

        var parameters = new List<string>
        {
            "MerchantLogin=" + Uri.EscapeDataString(shop.MerchantLogin),
            "OutSum=" + Uri.EscapeDataString(sum),
            "InvId=" + invoiceId.ToString(CultureInfo.InvariantCulture),
            "Description=" + Uri.EscapeDataString(description),
            "Culture=ru",
            "Encoding=utf-8"
        };

        // Состав чека. В адрес он уходит закодированным, а в подпись входит
        // тем же минимизированным JSON, каким был построен, — без кодирования.
        // Это самое частое место ошибки: подписанная закодированная строка
        // даёт ошибку 29 «неверный параметр SignatureValue», и выглядит она
        // как проблема с магазином, а не с подписью.
        string? receiptJson = null;
        if (shop.SendReceipt)
        {
            receiptJson = BuildReceiptJson(shop, amount, description);
            parameters.Add("Receipt=" + Uri.EscapeDataString(receiptJson));
        }

        if (!string.IsNullOrWhiteSpace(email))
            parameters.Add("Email=" + Uri.EscapeDataString(email));

        if (recurring)
            parameters.Add("Recurring=true");

        if (shop.IsTest)
            parameters.Add("IsTest=1");

        var signatureSource = receiptJson is null
            ? $"{shop.MerchantLogin}:{sum}:{invoiceId}:{shop.Password1}"
            : $"{shop.MerchantLogin}:{sum}:{invoiceId}:{receiptJson}:{shop.Password1}";

        parameters.Add("SignatureValue=" + Md5(signatureSource));

        return shop.PaymentUrl + "?" + string.Join("&", parameters);
    }

    /// <summary>
    /// Проверяет подпись уведомления об оплате (ResultURL).
    /// Сумма и номер счёта берутся из запроса как есть: Робокасса считает
    /// подпись по присланным строкам, а не по нашим представлениям о них.
    /// </summary>
    public bool VerifyResultSignature(string? outSum, string? invoiceId, string? signature)
        => VerifyResultSignature(_options, outSum, invoiceId, signature);

    public static bool VerifyResultSignature(
        RobokassaOptions shop, string? outSum, string? invoiceId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(outSum) ||
            string.IsNullOrWhiteSpace(invoiceId) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Md5($"{outSum}:{invoiceId}:{shop.Password2}");

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    /// <summary>Совпадает ли оплаченная сумма с ценой лицензии.</summary>
    public bool IsExpectedAmount(string? outSum) => IsExpectedAmount(outSum, _options.Amount);

    /// <summary>Совпадает ли оплаченная сумма с той, на которую выставлен счёт.</summary>
    public static bool IsExpectedAmount(string? outSum, decimal expected)
        => decimal.TryParse(outSum, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
           && value == expected;

    /// <summary>
    /// Повторное списание по ранее оплаченному счёту.
    ///
    /// Форму покупателю не показывают: Робокасса списывает с карты, которой
    /// он платил в первый раз, и присылает обычное уведомление на ResultURL —
    /// дальше всё идёт по общему пути.
    /// </summary>
    public async Task<bool> ChargeRecurringAsync(
        HttpClient http, long invoiceId, long previousInvoiceId, decimal amount, string description,
        CancellationToken cancellationToken)
    {
        var sum = FormatSum(amount);

        var form = new Dictionary<string, string>
        {
            ["MerchantLogin"] = _board.MerchantLogin,
            ["InvoiceID"] = invoiceId.ToString(CultureInfo.InvariantCulture),
            ["PreviousInvoiceID"] = previousInvoiceId.ToString(CultureInfo.InvariantCulture),
            ["OutSum"] = sum,
            ["Description"] = description
        };

        // Состав чека — и здесь тоже. Продление это отдельная продажа со
        // своим чеком, и без номенклатуры он такой же неполный, как у
        // первой оплаты. Место чека в подписи то же, что и в ссылке на
        // оплату: между номером счёта и первым паролем. Кодирование формы
        // делает HttpClient, а подписывается тот же минимизированный
        // JSON — подписанная закодированная строка даёт ошибку 29, и
        // выглядит она как проблема с магазином, а не с подписью.
        string? receiptJson = null;
        if (_board.SendReceipt)
        {
            receiptJson = BuildReceiptJson(_board, amount, description);
            form["Receipt"] = receiptJson;
        }

        form["SignatureValue"] = Md5(
            receiptJson is null
                ? $"{_board.MerchantLogin}:{sum}:{invoiceId}:{_board.Password1}"
                : $"{_board.MerchantLogin}:{sum}:{invoiceId}:{receiptJson}:{_board.Password1}");

        using var response = await http.PostAsync(
            _board.RecurringUrl, new FormUrlEncodedContent(form), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Робокасса отвечает строкой: OK при успехе, иначе описание ошибки.
        return response.IsSuccessStatusCode
            && body.TrimStart().StartsWith("OK", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatSum(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string BuildReceiptJson(RobokassaOptions shop, decimal amount, string description)
    {
        var receipt = new Dictionary<string, object>();

        // Система налогообложения. У самозанятого её нет: перечень значений
        // Робокассы (osn, usn_income, …) режима НПД не содержит, а пустая
        // строка отклоняется так же, как неизвестное значение. Поэтому поле
        // не передаётся вовсе — тогда настройка берётся из кабинета магазина.
        if (!string.IsNullOrWhiteSpace(shop.TaxSystem))
            receipt["sno"] = shop.TaxSystem;

        receipt["items"] = new[]
        {
            new
            {
                name = description,
                quantity = 1,
                sum = amount,
                payment_method = "full_payment",
                payment_object = shop.PaymentObject,
                tax = shop.Tax
            }
        };

        return JsonSerializer.Serialize(receipt, ReceiptJson);
    }

    private static string Md5(string value)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
