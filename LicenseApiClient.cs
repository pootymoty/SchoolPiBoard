using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Whiteboard.Services;

/// <summary>Чем закончился разговор с сервером лицензий.</summary>
public enum LicenseCallStatus
{
    /// <summary>Ключ принят.</summary>
    Ok,

    /// <summary>Ключ не найден, отозван или не подходит этому устройству.</summary>
    InvalidKey,

    /// <summary>Лимит устройств исчерпан.</summary>
    DeviceLimit,

    /// <summary>До сервера не достучались: нет сети, таймаут, DNS.</summary>
    Offline,

    /// <summary>Сервер ответил, но не то, чего мы ждали (5xx, 429, мусор в теле).</summary>
    ServerError
}

public sealed class LicenseCallResult
{
    public LicenseCallStatus Status { get; init; }
    public string? Token { get; init; }
    public string? Email { get; init; }
    public DateTime? ActivatedAt { get; init; }
    public int DevicesUsed { get; init; }
    public int DeviceLimit { get; init; } = LicenseOptions.DefaultDeviceLimit;

    /// <summary>Готовое к показу сообщение об ошибке.</summary>
    public string Message { get; init; } = string.Empty;

    public bool IsOk => Status == LicenseCallStatus.Ok;
}

/// <summary>
/// Тонкий клиент к серверу лицензий. Наружу не выпускает исключений —
/// сетевые сбои превращаются в <see cref="LicenseCallStatus.Offline"/>,
/// потому что для приложения это не ошибка, а обычная ситуация.
/// </summary>
public sealed class LicenseApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _baseUrl;

    public LicenseApiClient(string baseUrl)
    {
        _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
    }

    public Task<LicenseCallResult> ActivateAsync(string key, string hardwareId, CancellationToken token = default)
        => SendAsync("/license/activate", key, hardwareId, token);

    public Task<LicenseCallResult> DeactivateAsync(string key, string hardwareId, CancellationToken token = default)
        => SendAsync("/license/deactivate", key, hardwareId, token);

    /// <summary>
    /// Фоновая проверка. Сервер отвечает 200 и полем valid — «нет» здесь
    /// означает именно отозванный ключ, а не проблему со связью.
    /// </summary>
    public Task<LicenseCallResult> ValidateAsync(string key, string hardwareId, CancellationToken token = default)
        => SendAsync("/license/validate", key, hardwareId, token);

    private async Task<LicenseCallResult> SendAsync(string path, string key, string hardwareId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.ServerError,
                Message = "В настройках не задан адрес сервера лицензий."
            };
        }

        try
        {
            var payload = JsonSerializer.Serialize(new { key, hardwareId });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(_baseUrl + path, content, token).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return Interpret(response.StatusCode, body);
        }
        catch (OperationCanceledException)
        {
            // Сюда же попадает таймаут HttpClient.
            return Offline();
        }
        catch (HttpRequestException)
        {
            return Offline();
        }
        catch (Exception ex)
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.ServerError,
                Message = "Не удалось обратиться к серверу лицензий: " + ex.Message
            };
        }
    }

    private static LicenseCallResult Offline() => new()
    {
        Status = LicenseCallStatus.Offline,
        Message = "Нет связи с сервером лицензий."
    };

    private static LicenseCallResult Interpret(HttpStatusCode statusCode, string body)
    {
        var fields = Fields.Parse(body);

        if (statusCode == HttpStatusCode.OK)
        {
            // /license/validate отвечает 200 и в случае отозванного ключа.
            if (fields.Valid == false)
            {
                return new LicenseCallResult
                {
                    Status = LicenseCallStatus.InvalidKey,
                    Message = fields.Message ?? "Лицензия больше не действует."
                };
            }

            return new LicenseCallResult
            {
                Status = LicenseCallStatus.Ok,
                Token = fields.Token,
                Email = fields.Email,
                ActivatedAt = fields.ActivatedAt,
                DevicesUsed = fields.DevicesUsed ?? 1,
                DeviceLimit = fields.DeviceLimit ?? LicenseOptions.DefaultDeviceLimit
            };
        }

        if (statusCode == HttpStatusCode.Forbidden || statusCode == HttpStatusCode.NotFound)
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.InvalidKey,
                Message = fields.Message ?? "Ключ не найден или отозван."
            };
        }

        if (statusCode == HttpStatusCode.Conflict)
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.DeviceLimit,
                DevicesUsed = fields.DevicesUsed ?? LicenseOptions.DefaultDeviceLimit,
                DeviceLimit = fields.DeviceLimit ?? LicenseOptions.DefaultDeviceLimit,
                Message = fields.Message ?? "Ключ уже используется на другом компьютере."
            };
        }

        if (statusCode == HttpStatusCode.BadRequest)
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.InvalidKey,
                Message = fields.Message ?? "Ключ введён не полностью."
            };
        }

        if ((int)statusCode == 429)
        {
            return new LicenseCallResult
            {
                Status = LicenseCallStatus.ServerError,
                Message = "Слишком много попыток подряд. Подождите минуту и попробуйте снова."
            };
        }

        return new LicenseCallResult
        {
            Status = LicenseCallStatus.ServerError,
            Message = fields.Message ?? $"Сервер лицензий ответил ошибкой ({(int)statusCode})."
        };
    }

    /// <summary>Разбор ответа «как получится»: чего нет — того нет.</summary>
    private sealed class Fields
    {
        public string? Token { get; private init; }
        public string? Email { get; private init; }
        public string? Message { get; private init; }
        public bool? Valid { get; private init; }
        public int? DevicesUsed { get; private init; }
        public int? DeviceLimit { get; private init; }
        public DateTime? ActivatedAt { get; private init; }

        public static Fields Parse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return new Fields();

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new Fields();

                return new Fields
                {
                    Token = ReadString(root, "token"),
                    Email = ReadString(root, "email"),
                    Message = ReadString(root, "message"),
                    Valid = ReadBool(root, "valid"),
                    DevicesUsed = ReadInt(root, "devicesUsed"),
                    DeviceLimit = ReadInt(root, "deviceLimit"),
                    ActivatedAt = ReadDate(root, "activatedAt")
                };
            }
            catch (JsonException)
            {
                return new Fields();
            }
        }

        private static string? ReadString(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static bool? ReadBool(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value))
                return null;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }

        private static int? ReadInt(JsonElement root, string name)
            => root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out var number)
                ? number
                : null;

        private static DateTime? ReadDate(JsonElement root, string name)
            => root.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
               && value.TryGetDateTime(out var date)
                ? date.ToUniversalTime()
                : null;
    }
}
