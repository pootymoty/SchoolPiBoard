using Microsoft.AspNetCore.Mvc;
using SchoolPiBoard.LicenseServer.Services;

namespace SchoolPiBoard.LicenseServer.Endpoints;

/// <summary>Запрос пробного периода от приложения.</summary>
public sealed record TrialRequest(string? HardwareId, string? Email);

public static class TrialEndpoints
{
    public static void MapTrialEndpoints(this WebApplication app)
    {
        app.MapPost("/trial/start", async (
            [FromBody] TrialRequest request,
            [FromServices] TrialService trials,
            CancellationToken cancellationToken) =>
        {
            var result = await trials.StartAsync(request.HardwareId, request.Email, cancellationToken);

            return result.Outcome switch
            {
                TrialOutcome.Started or TrialOutcome.Resumed => Results.Ok(new
                {
                    status = result.Outcome == TrialOutcome.Started ? "started" : "resumed",
                    startedAt = result.StartedAt,
                    expiresAt = result.ExpiresAt,
                    days = result.Days
                }),

                TrialOutcome.AlreadyUsed => Results.Json(new
                {
                    error = "trial_used",
                    message = $"Пробный период уже использован — {result.Days} дня даются один раз. " +
                              "Чтобы продолжить работу, введите купленный ключ.",
                    expiresAt = result.ExpiresAt
                }, statusCode: StatusCodes.Status409Conflict),

                _ => Results.Json(new
                {
                    error = "bad_request",
                    message = "Нужен корректный адрес почты."
                }, statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .RequireRateLimiting(LicenseEndpoints.ActivatePolicy);
    }
}
