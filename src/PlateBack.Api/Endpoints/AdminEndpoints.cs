using PlateBack.Api.Extensions;
using PlateBack.Application.Features.Admin;

namespace PlateBack.Api.Endpoints;

internal static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").WithTags("Admin").RequireAuthorization(AuthPolicies.Admin);

        admin.MapGet("/organizations", async (string? status, int? page, int? pageSize, GetOrganizationsForReviewHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(status, page, pageSize, ct)))
            .WithSummary("Review queue")
            .WithDescription("Businesses by status (Pending, Verified, Rejected, Suspended), oldest first, with the owner's name and email.");

        admin.MapPost("/organizations/{id:guid}/verify", async (Guid id, ReviewOrganizationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.VerifyAsync(id, ct)))
            .WithSummary("Verify a business")
            .WithDescription("Also lifts a suspension. The owners are notified.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPost("/organizations/{id:guid}/reject", async (Guid id, ReasonRequest request, ReviewOrganizationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.RejectAsync(id, request, ct)))
            .Validate<ReasonRequest>()
            .WithSummary("Reject a pending business")
            .WithDescription("A reason is required and is shown to the owner.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPost("/organizations/{id:guid}/suspend", async (Guid id, ReasonRequest request, ReviewOrganizationHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.SuspendAsync(id, request, ct)))
            .Validate<ReasonRequest>()
            .WithSummary("Suspend a verified business")
            .WithDescription("A reason is required. Its live listings and their reservations are cancelled.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapGet("/users", async (string? q, int? page, int? pageSize, SearchUsersHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(q, page, pageSize, ct)))
            .WithSummary("Search accounts")
            .WithDescription("By part of the name or by exact email.");

        admin.MapPost("/users/{id:guid}/suspend", async (Guid id, ReasonRequest request, SuspendUserHandler handler, CancellationToken ct) =>
            {
                await handler.SuspendAsync(id, request, ct);
                return TypedResults.NoContent();
            })
            .Validate<ReasonRequest>()
            .WithSummary("Suspend an account")
            .WithDescription("A reason is required. Ends the account's sessions; it can no longer log in or reserve.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("/users/{id:guid}/restore", async (Guid id, SuspendUserHandler handler, CancellationToken ct) =>
            {
                await handler.RestoreAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithSummary("Restore a suspended account")
            .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("/listings/{id:guid}/remove", async (Guid id, ReasonRequest request, RemoveListingHandler handler, CancellationToken ct) =>
            {
                await handler.HandleAsync(id, request, ct);
                return TypedResults.NoContent();
            })
            .Validate<ReasonRequest>()
            .WithSummary("Remove a listing")
            .WithDescription("A reason is required. Its reservations are cancelled and the business is notified.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapGet("/audit-logs", async (string? entityType, Guid? entityId, Guid? actorId, int? page, int? pageSize, GetAuditLogsHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(entityType, entityId, actorId, page, pageSize, ct)))
            .WithSummary("Audit log")
            .WithDescription("Every state change, newest first. Filter by entityType, entityId or actorId.");
    }
}
