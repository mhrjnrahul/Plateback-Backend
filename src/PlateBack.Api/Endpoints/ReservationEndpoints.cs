using PlateBack.Api.Extensions;
using PlateBack.Application.Features.Reservations;

namespace PlateBack.Api.Endpoints;

internal static class ReservationEndpoints
{
    public static void MapReservationEndpoints(this RouteGroupBuilder api)
    {
        var reservations = api.MapGroup("/reservations").WithTags("Reservations").RequireAuthorization();

        reservations.MapGet("/mine", async (string? status, int? page, int? pageSize, GetMyReservationsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(status, page, pageSize, ct)))
            .WithSummary("My reservations")
            .WithDescription("status=active (default, soonest pickup first) or status=history (most recent first).");

        reservations.MapGet("/{id:guid}", async (Guid id, GetReservationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, ct)))
            .WithSummary("One reservation")
            .WithDescription("For the customer or a member of the listing's business; 404 for anyone else.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        reservations.MapPost("/{id:guid}/cancel", async (Guid id, CancelReservationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, ct)))
            .WithSummary("Cancel my reservation")
            .WithDescription("Free before the pickup window opens; after that it is a late cancellation (-5 reliability). Stock goes back while the listing is open.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        reservations.MapPost("/verify", async (VerifyPickupRequest request, VerifyPickupHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(request, ct)))
            .Validate<VerifyPickupRequest>()
            .RequireRateLimiting(RateLimits.PickupCodes)
            .WithSummary("Hand over food with a pickup code")
            .WithDescription("Staff enter the customer's code (case and spaces do not matter). Codes from other businesses fail like unknown codes. Rate limited per user.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }
}
