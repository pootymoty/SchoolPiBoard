using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.LicenseServer.Services;

namespace SchoolPiBoard.LicenseServer.Endpoints;

/// <summary>Тело запросов от десктопного клиента.</summary>
public sealed record LicenseRequest(string? Key, string? HardwareId);

public static class LicenseEndpoints
{
    /// <summary>Политика ограничения частоты для активации — защита от перебора ключей.</summary>
    public const string ActivatePolicy = "license-activate";

    /// <summary>Проверок больше: их шлёт каждый установленный клиент раз в сутки.</summary>
    public const string ValidatePolicy = "license-validate";

    public static void MapLicenseEndpoints(this WebApplication app)
    {
        app.MapPost("/license/activate", async (
            [FromBody] LicenseRequest request,
            [FromServices] LicenseService licenses,
            [FromServices] TokenService tokens,
            CancellationToken cancellationToken) =>
        {
            var result = await licenses.ActivateAsync(request.Key, request.HardwareId, cancellationToken);

            switch (result.Outcome)
            {
                case ActivationOutcome.Activated:
                    var license = result.License!;
                    var activation = result.Activation!;
                    return Results.Ok(new
                    {
                        token = tokens.Issue(license.Id, license.Key, activation.HardwareId),
                        key = license.Key,
                        email = license.Email,
                        activatedAt = activation.ActivatedAt,
                        devicesUsed = result.DevicesUsed,
                        deviceLimit = result.DeviceLimit
                    });

                case ActivationOutcome.DeviceLimitReached:
                    return Results.Json(new
                    {
                        error = "device_limit",
                        message = $"Ключ уже используется на {result.DeviceLimit} компьютерах. " +
                                  "Освободите слот на одном из них и попробуйте снова.",
                        devicesUsed = result.DevicesUsed,
                        deviceLimit = result.DeviceLimit
                    }, statusCode: StatusCodes.Status409Conflict);

                case ActivationOutcome.BadRequest:
                    return Results.Json(new
                    {
                        error = "bad_request",
                        message = "Ключ должен состоять из 16 символов в формате XXXX-XXXX-XXXX-XXXX."
                    }, statusCode: StatusCodes.Status400BadRequest);

                default:
                    return Results.Json(new
                    {
                        error = "invalid_key",
                        message = "Такой ключ не найден или он отозван."
                    }, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .RequireRateLimiting(ActivatePolicy);

        app.MapPost("/license/validate", async (
            [FromBody] LicenseRequest request,
            [FromServices] LicenseService licenses,
            CancellationToken cancellationToken) =>
        {
            var result = await licenses.ValidateAsync(request.Key, request.HardwareId, cancellationToken);

            // Отвечаем 200 в обоих случаях: для клиента важно отличать
            // «сервер сказал нет» от «до сервера не достучались».
            return Results.Ok(new
            {
                valid = result.Valid,
                reason = result.Reason,
                devicesUsed = result.DevicesUsed,
                deviceLimit = result.DeviceLimit,
                message = result.Valid ? null : DescribeValidationFailure(result.Reason)
            });
        })
        .RequireRateLimiting(ValidatePolicy);

        app.MapPost("/license/deactivate", async (
            [FromBody] LicenseRequest request,
            [FromServices] LicenseService licenses,
            CancellationToken cancellationToken) =>
        {
            var outcome = await licenses.DeactivateAsync(request.Key, request.HardwareId, cancellationToken);

            return outcome switch
            {
                DeactivationOutcome.Released => Results.Ok(new { ok = true, reason = "released" }),

                // Слот и так свободен — для клиента это успех.
                DeactivationOutcome.NotActivated => Results.Ok(new { ok = true, reason = "not_activated" }),

                DeactivationOutcome.BadRequest => Results.Json(new
                {
                    error = "bad_request",
                    message = "Ключ должен состоять из 16 символов в формате XXXX-XXXX-XXXX-XXXX."
                }, statusCode: StatusCodes.Status400BadRequest),

                _ => Results.Json(new
                {
                    error = "invalid_key",
                    message = "Такой ключ не найден."
                }, statusCode: StatusCodes.Status403Forbidden)
            };
        })
        .RequireRateLimiting(ActivatePolicy);
    }

    private static string DescribeValidationFailure(string reason) => reason switch
    {
        "revoked" => "Лицензия отозвана.",
        "unknown_key" => "Такой ключ не найден.",
        "device_not_activated" => "Этот компьютер отвязан от лицензии. Введите ключ заново.",
        _ => "Лицензия не подтверждена."
    };
}
