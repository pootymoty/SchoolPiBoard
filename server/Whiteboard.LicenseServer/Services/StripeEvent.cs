using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// То немногое, что нам нужно от события Stripe: тип, идентификатор платежа
/// и почта покупателя. Разбираем JSON вручную — из всего объёма события
/// используются три поля, ради них незачем тянуть SDK.
/// </summary>
public sealed class StripeEvent
{
    public required string Type { get; init; }

    /// <summary>Идентификатор платежа (payment intent или сессии оплаты).</summary>
    public string? PaymentId { get; init; }

    public string? Email { get; init; }

    /// <summary>Оплата действительно прошла.</summary>
    public bool Paid { get; init; }

    /// <summary>Событие, по которому положено выпускать ключ.</summary>
    public bool IsPurchase =>
        Paid &&
        !string.IsNullOrWhiteSpace(PaymentId) &&
        Type is "checkout.session.completed" or "payment_intent.succeeded";

    public static bool TryParse(string payload, out StripeEvent? result)
    {
        result = null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var type = ReadString(root, "type");
            if (string.IsNullOrWhiteSpace(type))
                return false;

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("object", out var payment))
            {
                result = new StripeEvent { Type = type, Paid = false };
                return true;
            }

            result = type switch
            {
                "checkout.session.completed" => new StripeEvent
                {
                    Type = type,
                    // payment_intent — то, что живёт дальше сессии; если его нет
                    // (например, оплата ещё обрабатывается), берём id сессии.
                    PaymentId = ReadString(payment, "payment_intent") ?? ReadString(payment, "id"),
                    Email = ReadCustomerEmail(payment),
                    Paid = ReadString(payment, "payment_status") == "paid"
                },

                "payment_intent.succeeded" => new StripeEvent
                {
                    Type = type,
                    PaymentId = ReadString(payment, "id"),
                    Email = ReadString(payment, "receipt_email") ?? ReadChargeEmail(payment),
                    Paid = true
                },

                _ => new StripeEvent { Type = type, Paid = false }
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>SHA-256 от идентификатора платежа: в базе хранится только он.</summary>
    public static string HashPaymentId(string paymentId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paymentId))).ToLowerInvariant();

    private static string? ReadCustomerEmail(JsonElement payment)
    {
        if (payment.TryGetProperty("customer_details", out var details))
        {
            var email = ReadString(details, "email");
            if (!string.IsNullOrWhiteSpace(email))
                return email;
        }

        return ReadString(payment, "customer_email");
    }

    private static string? ReadChargeEmail(JsonElement payment)
    {
        if (!payment.TryGetProperty("charges", out var charges) ||
            !charges.TryGetProperty("data", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var charge in items.EnumerateArray())
        {
            if (charge.TryGetProperty("billing_details", out var billing))
            {
                var email = ReadString(billing, "email");
                if (!string.IsNullOrWhiteSpace(email))
                    return email;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
