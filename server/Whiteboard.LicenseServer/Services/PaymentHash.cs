using System.Security.Cryptography;
using System.Text;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Хеш идентификатора платежа. В базе лежит только он: по нему видно
/// повторную доставку уведомления, но сам идентификатор платежа
/// (и тем более данные карты) сервис не хранит.
/// </summary>
public static class PaymentHash
{
    public static string Of(string paymentId)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paymentId))).ToLowerInvariant();

    /// <summary>Номер счёта Робокассы в том виде, в каком он попадает в базу.</summary>
    public static string ForRobokassa(long invoiceId) => Of($"robokassa:{invoiceId}");

    public static string ForStripe(string paymentIntentId) => Of($"stripe:{paymentIntentId}");
}
