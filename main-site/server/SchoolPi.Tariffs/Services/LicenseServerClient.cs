using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SchoolPi.Tariffs.Configuration;

namespace SchoolPi.Tariffs.Services;

/// <summary>Счёт, выставленный сервером ключей.</summary>
public sealed record Invoice(string InvoiceId, string PaymentUrl, string Amount);

/// <summary>
/// Связь с сервером ключей.
///
/// Этот сервис не знает паролей Робокассы и никогда к ней не
/// обращается — платёжное целиком живёт на сервере ключей, том же
/// самом, что уже продаёт лицензии на офлайн-доску и подписки на
/// онлайн-доску. У тарифов там свой, третий магазин.
///
/// Копия my_app/routes/payment.py и KeyServerClient.cs из
/// schoolpiboard_online — тот же протокол подписи.
/// </summary>
public sealed class LicenseServerClient
{
    public const string TimestampHeader = "X-Timestamp";
    public const string SignatureHeader = "X-Signature";

    public static readonly TimeSpan SignatureLifetime = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly LicenseServerOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<LicenseServerClient> _log;

    public LicenseServerClient(LicenseServerOptions options, IHttpClientFactory http, ILogger<LicenseServerClient> log)
    {
        _options = options;
        _http = http;
        _log = log;
    }

    public bool IsConfigured => _options.IsConfigured;

    public static string Sign(string secret, string timestamp, string body)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var payload = Encoding.UTF8.GetBytes(timestamp + "." + body);

        return Convert.ToHexString(HMACSHA256.HashData(key, payload)).ToLowerInvariant();
    }

    /// <summary>Проверяет подпись входящего сообщения об оплате (от сервера ключей).</summary>
    public bool Verify(string? timestamp, string? signature, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.SharedSecret)
            || string.IsNullOrWhiteSpace(timestamp)
            || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!long.TryParse(timestamp, out var unix)) return false;

        var moment = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((DateTimeOffset.UtcNow - moment).Duration() > SignatureLifetime) return false;

        var expected = Sign(_options.SharedSecret, timestamp, body);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public Task<Invoice?> CreateInvoiceAsync(
        long userId, string email, string planKey, string planName, int days, decimal amount, bool autoRenew,
        CancellationToken cancellationToken)
        => SendAsync<Invoice>("/tariffs/invoice", new
        {
            userId,
            email,
            planCode = planKey,
            planName,
            days,
            amount,
            autoRenew
        }, cancellationToken);

    /// <summary>Просит списать повторно по ранее оплаченному счёту.</summary>
    public Task<Invoice?> ChargeRecurringAsync(
        long userId, string planKey, string planName, int days, decimal amount, long previousInvoiceId,
        CancellationToken cancellationToken)
        => SendAsync<Invoice>("/tariffs/recurring", new
        {
            userId,
            planCode = planKey,
            planName,
            days,
            amount,
            previousInvoiceId
        }, cancellationToken);

    private async Task<T?> SendAsync<T>(string path, object payload, CancellationToken cancellationToken)
        where T : class
    {
        if (!IsConfigured)
        {
            _log.LogError("Связь с сервером ключей не настроена — оплата тарифов невозможна.");
            return null;
        }

        var body = JsonSerializer.Serialize(payload, Json);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Url + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Add(TimestampHeader, timestamp);
        request.Headers.Add(SignatureHeader, Sign(_options.SharedSecret, timestamp, body));

        try
        {
            using var response = await _http.CreateClient().SendAsync(request, cancellationToken);
            var answer = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogError(
                    "Сервер ключей отказал по {Path}: {Status} {Body}",
                    path, (int)response.StatusCode, answer);
                return null;
            }

            return JsonSerializer.Deserialize<T>(answer, Json);
        }
        catch (Exception error)
        {
            _log.LogError(error, "Сервер ключей недоступен по {Path}.", path);
            return null;
        }
    }
}
