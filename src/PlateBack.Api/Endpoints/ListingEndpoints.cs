using PlateBack.Api.Extensions;
using PlateBack.Application.Features.Listings;
using PlateBack.Application.Features.Reservations;

namespace PlateBack.Api.Endpoints;

internal static class ListingEndpoints
{
    public static void MapListingEndpoints(this RouteGroupBuilder api)
    {
        var listings = api.MapGroup("/listings").WithTags("Listings");

        listings.MapGet("/", async ([AsParameters] SearchListingsQuery query, SearchListingsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(query, ct)))
            .Validate<SearchListingsQuery>()
            .WithSummary("Search listings")
            .WithDescription("Live listings from verified businesses. With lat and lng: within radiusKm (default 5, maximum 25), sorted by distance by default. Sold-out listings come last.");

        listings.MapGet("/{id:guid}", async (Guid id, GetListingHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, ct)))
            .WithSummary("One listing")
            .WithDescription("Includes live stock, the allergen note and how many units one person may reserve.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        listings.MapPut("/{id:guid}", async (Guid id, ListingInput input, UpdateListingHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, input, ct)))
            .RequireAuthorization()
            .Validate<ListingInput>()
            .WithSummary("Edit a listing")
            .WithDescription("Members only, live listings only. Quantity cannot drop below the units already reserved. 409 CONFLICT if the listing changed while you edited it.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        listings.MapPost("/{id:guid}/cancel", async (Guid id, CancelListingHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, ct)))
            .RequireAuthorization()
            .WithSummary("Cancel a listing")
            .WithDescription("Members only. Its reservations are cancelled without penalty and the customers are notified.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        listings.MapPost("/{id:guid}/reservations", async (Guid id, CreateReservationRequest request, ReserveListingHandler handler, CancellationToken ct) =>
            {
                var reservation = await handler.HandleAsync(id, request, ct);
                return TypedResults.Created($"/api/v1/reservations/{reservation.Id}", reservation);
            })
            .RequireAuthorization()
            .Validate<CreateReservationRequest>()
            .WithTags("Reservations")
            .WithSummary("Reserve")
            .WithDescription("Returns the pickup code. 409 SOLD_OUT when the stock went to someone else first, LIMIT_REACHED when the customer already holds their maximum, WINDOW_CLOSED when the listing has ended.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapGet("/stats/impact", async (GetImpactStatsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .CacheOutput(CachePolicies.Impact)
            .WithTags("Stats")
            .WithSummary("Platform impact numbers")
            .WithDescription("Meals rescued, partner businesses and live listings. Cached for 5 minutes.");
    }
}
