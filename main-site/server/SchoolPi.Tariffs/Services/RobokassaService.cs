using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SchoolPi.Tariffs.Configuration;

namespace SchoolPi.Tariffs.Services;

/// <summary>
/// Ссылка на оплату и проверка ответа платёжной системы.
///
/// Копия того же приёма, что уже работает у online/server (RobokassaService
/// там) — тот же протокол, тот же порядок полей в подписи, тот же вид чека.
/// Переиспользовать код напрямую нельзя (разные проекты, разный DI), но
/// логика намеренно не менялась ни на строку, чтобы не завести здесь
/// собственный, ещё не проверенный на боевом трафике вариант интеграции.
/// </summary>
public sealed class RobokassaService
{
    private readonly PaymentOptions _options;

    public RobokassaService(PaymentOptions options)
    {
        _options = options;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string BuildPaymentUrl(long invoiceId, decimal amount, string description, string? email)
    {
        var sum = FormatSum(amount);

        var parameters = new List<string>
        {
            "MerchantLogin=" + Uri.EscapeDataString(_options.MerchantLogin),
            "OutSum=" + Uri.EscapeDataString(sum),
            "InvId=" + invoiceId.ToString(CultureInfo.InvariantCulture),
            "Description=" + Uri.EscapeDataString(description),
            "Culture=ru",
            "Encoding=utf-8"
        };

        string? encodedReceipt = null;
        if (_options.SendReceipt)
        {
            encodedReceipt = Uri.EscapeDataString(BuildReceiptJson(amount, description));
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

    public bool VerifyResultSignature(string? outSum, string? invoiceId, string? signature)
    {
        if (string.IsNullOrWhiteSpace(outSum) || string.IsNullOrWhiteSpace(invoiceId) || string.IsNullOrWhiteSpace(signature))
            return false;

        var expected = Md5($"{outSum}:{invoiceId}:{_options.Password2}");

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public static string FormatSum(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private string BuildReceiptJson(decimal amount, string description) => JsonSerializer.Serialize(new
    {
        sno = _options.TaxSystem,
        items = new[]
        {
            new
            {
                name = description,
                quantity = 1,
                sum = amount,
                payment_method = "full_payment",
                payment_object = "service",
                tax = _options.Tax
            }
        }
    });

    private static string Md5(string value)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
