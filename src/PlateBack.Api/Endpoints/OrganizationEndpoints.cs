using PlateBack.Api.Extensions;
using PlateBack.Application.Features.Listings;
using PlateBack.Application.Features.Organizations;
using PlateBack.Application.Features.Reservations;

namespace PlateBack.Api.Endpoints;

internal static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var orgs = api.MapGroup("/organizations").WithTags("Organizations");

        orgs.MapPost("/", async (OrganizationRequest request, CreateOrganizationHandler handler, CancellationToken ct) =>
            {
                var organization = await handler.HandleAsync(request, ct);
                return TypedResults.Created($"/api/v1/organizations/{organization.Id}", organization);
            })
            .RequireAuthorization()
            .Validate<OrganizationRequest>()
            .WithSummary("Register a business")
            .WithDescription("Starts as Pending until an admin verifies it. The caller becomes its Owner.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        orgs.MapGet("/mine", async (GetMyOrganizationsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .RequireAuthorization()
            .WithSummary("Businesses you belong to")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        orgs.MapGet("/{id:guid}", async (Guid id, GetOrganizationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, ct)))
            .AllowAnonymous()
            .WithSummary("One business")
            .WithDescription("Public when verified. Pending, rejected or suspended businesses are visible only to their members and admins; anyone else gets 404.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        orgs.MapPatch("/{id:guid}", async (Guid id, OrganizationRequest request, UpdateOrganizationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, request, ct)))
            .RequireAuthorization()
            .Validate<OrganizationRequest>()
            .WithSummary("Edit business details")
            .WithDescription("Owners only.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orgs.MapGet("/{id:guid}/members", async (Guid id, OrganizationMembersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.ListAsync(id, ct)))
            .RequireAuthorization()
            .WithSummary("The team")
            .WithDescription("Owners only.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orgs.MapPost("/{id:guid}/members", async (Guid id, AddMemberRequest request, OrganizationMembersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.AddAsync(id, request, ct)))
            .RequireAuthorization()
            .Validate<AddMemberRequest>()
            .WithSummary("Add a staff member by email")
            .WithDescription("Owners only. The person must already have a PlateBack account. Returns the updated team.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orgs.MapDelete("/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, OrganizationMembersHandler handler, CancellationToken ct) =>
            {
                await handler.RemoveAsync(id, userId, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization()
            .WithSummary("Remove a member")
            .WithDescription("Owners only. The last owner cannot be removed (409 INVALID_STATE).")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orgs.MapGet("/{id:guid}/stats", async (Guid id, GetOrganizationStatsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, ct)))
            .RequireAuthorization()
            .WithSummary("Dashboard numbers")
            .WithDescription("Members only. Active listings, portions reserved, collected today, meals rescued this month and a 14-day series. Days follow the business's time zone.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orgs.MapGet("/{id:guid}/listings", async (Guid id, string? status, int? page, int? pageSize, GetOrganizationListingsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, status, page, pageSize, ct)))
            .RequireAuthorization()
            .WithSummary("The business's listings")
            .WithDescription("Members only. Optional status filter. Open listings first (soonest pickup), then past ones.")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        orgs.MapPost("/{id:guid}/listings", async (Guid id, ListingInput input, CreateListingHandler handler, CancellationToken ct) =>
            {
                var listing = await handler.HandleAsync(id, input, ct);
                return TypedResults.Created($"/api/v1/listings/{listing.Id}", listing);
            })
            .RequireAuthorization()
            .Validate<ListingInput>()
            .WithSummary("Publish a listing")
            .WithDescription("Members of a verified business only (403 ORG_NOT_VERIFIED otherwise). The pickup window must be 15 minutes to 12 hours and end in the future.")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        orgs.MapGet("/{id:guid}/reservations", async (Guid id, Guid? listingId, string? status, int? page, int? pageSize, GetOrganizationReservationsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(id, listingId, status, page, pageSize, ct)))
            .RequireAuthorization()
            .WithSummary("Today's counter queue")
            .WithDescription("Members only. Reservations for pickups ending today or later; waiting ones first.")
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
