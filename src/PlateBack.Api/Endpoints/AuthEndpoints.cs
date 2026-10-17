using PlateBack.Api.Extensions;
using PlateBack.Application.Features.Auth;
using PlateBack.Application.Features.Users;

namespace PlateBack.Api.Endpoints;

internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireRateLimiting(RateLimits.Auth);

        auth.MapPost("/register", async (RegisterRequest request, RegisterHandler handler, HttpResponse response, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                RefreshCookie.Set(response, result);
                return TypedResults.Created("/api/v1/users/me", result.Response);
            })
            .Validate<RegisterRequest>()
            .WithSummary("Create a customer account")
            .WithDescription("Creates the account, signs it in and sets the refresh-token cookie. Rate limited per IP address.")
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/login", async (LoginRequest request, LoginHandler handler, HttpResponse response, CancellationToken ct) =>
            {
                var result = await handler.HandleAsync(request, ct);
                RefreshCookie.Set(response, result);
                return TypedResults.Ok(result.Response);
            })
            .Validate<LoginRequest>()
            .WithSummary("Log in")
            .WithDescription("Returns a 15-minute access token and sets a 7-day refresh-token cookie. Five wrong passwords lock the account for 15 minutes (429 RATE_LIMITED).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/refresh", async (RefreshHandler handler, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            {
                try
                {
                    var result = await handler.HandleAsync(RefreshCookie.Read(request), ct);
                    RefreshCookie.Set(response, result);
                    return TypedResults.Ok(result.Response);
                }
                catch
                {
                    RefreshCookie.Clear(response);
                    throw;
                }
            })
            .WithSummary("Get a new access token")
            .WithDescription("Uses the refresh-token cookie, rotates it and returns a new access token. Reusing an old refresh token ends every session of that account.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/logout", async (LogoutHandler handler, HttpRequest request, HttpResponse response, CancellationToken ct) =>
            {
                await handler.HandleAsync(RefreshCookie.Read(request), ct);
                RefreshCookie.Clear(response);
                return TypedResults.NoContent();
            })
            .WithSummary("Log out")
            .WithDescription("Revokes the current refresh token and clears the cookie.");

        var users = api.MapGroup("/users").WithTags("Users").RequireAuthorization();

        users.MapGet("/me", async (GetCurrentUserHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(ct)))
            .WithSummary("The signed-in user")
            .WithDescription("Profile, reliability score and business memberships.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        users.MapPatch("/me", async (UpdateProfileRequest request, UpdateProfileHandler handler, CancellationToken ct) =>
                TypedResults.Ok(await handler.HandleAsync(request, ct)))
            .Validate<UpdateProfileRequest>()
            .WithSummary("Change your display name")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
