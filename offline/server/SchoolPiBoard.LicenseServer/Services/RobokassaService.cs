using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SchoolPiBoard.LicenseServer.Configuration;

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

    public RobokassaService(RobokassaOptions options)
    {
        _options = options;
    }

    public decimal Amount => _options.Amount;

    /// <summary>Ссылка, на которую нужно отправить покупателя.</summary>
    public string BuildPaymentUrl(long invoiceId, string email)
    {
        var sum = FormatSum(_options.Amount);

        var parameters = new List<string>
        {
            "MerchantLogin=" + Uri.EscapeDataString(_options.MerchantLogin),
            "OutSum=" + Uri.EscapeDataString(sum),
            "InvId=" + invoiceId.ToString(CultureInfo.InvariantCulture),
            "Description=" + Uri.EscapeDataString(_options.Description),
            "Culture=ru",
            "Encoding=utf-8"
        };

        // Состав чека. Робокасса требует его уже закодированным, и в подпись
        // попадает ровно та же закодированная строка — это место, на котором
        // чаще всего ломается интеграция.
        string? encodedReceipt = null;
        if (_options.SendReceipt)
        {
            encodedReceipt = Uri.EscapeDataString(BuildReceiptJson());
            parameters.Add("Receipt=" + encodedReceipt);
        }

        if (!string.IsNullOrWhiteSpace(email))
            parameters.Add("Email=" + Uri.EscapeDataString(email));

        if (_options.IsTest)
            parameters.Add("IsTest=1");

        var signatureSource = encodedReceipt is null
            ? $"{_options.MerchantLogin}:{sum}:{invoiceId}:{_options.Password1}"
            : $"{_options.MerchantLogin}:{sum}:{invoiceId}:{encodedReceipt}:{_options.Password1}";

        parameters.Add("SignatureValue=" + Md5(signatureSource));

        return _options.PaymentUrl + "?" + string.Join("&", parameters);
    }

    /// <summary>
    /// Проверяет подпись уведомления об оплате (ResultURL).
    /// Сумма и номер счёта берутся из запроса как есть: Робокасса считает
    /// подпись по присланным строкам, а не по нашим представлениям о них.
    /// </summary>
    public bool VerifyResultSignature(string? outSum, string? invoiceId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(outSum) ||
            string.IsNullOrWhiteSpace(invoiceId) ||
            string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var expected = Md5($"{outSum}:{invoiceId}:{_options.Password2}");

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    /// <summary>Совпадает ли оплаченная сумма с ценой лицензии.</summary>
    public bool IsExpectedAmount(string? outSum)
        => decimal.TryParse(outSum, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
           && value == _options.Amount;

    public static string FormatSum(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private string BuildReceiptJson()
    {
        var receipt = new Dictionary<string, object>();

        // Система налогообложения. У самозанятого её нет: перечень значений
        // Робокассы (osn, usn_income, …) режима НПД не содержит, а пустая
        // строка отклоняется так же, как неизвестное значение. Поэтому поле
        // не передаётся вовсе — тогда настройка берётся из кабинета магазина.
        if (!string.IsNullOrWhiteSpace(_options.TaxSystem))
            receipt["sno"] = _options.TaxSystem;

        receipt["items"] = new[]
        {
            new
            {
                name = _options.Description,
                quantity = 1,
                sum = _options.Amount,
                payment_method = "full_payment",
                payment_object = _options.PaymentObject,
                tax = _options.Tax
            }
        };

        return JsonSerializer.Serialize(receipt, ReceiptJson);
    }

    private static string Md5(string value)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
