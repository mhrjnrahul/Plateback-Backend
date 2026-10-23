using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PlateBack.Api.Endpoints;
using PlateBack.Api.Extensions;
using PlateBack.Api.Middleware;
using PlateBack.Api.Realtime;
using PlateBack.Application;
using PlateBack.Application.Common.Abstractions;
using PlateBack.Application.Common.Errors;
using PlateBack.Application.Common.Options;
using PlateBack.Domain.Common;
using PlateBack.Infrastructure;
using PlateBack.Infrastructure.Identity;
using PlateBack.Infrastructure.Persistence;
using PlateBack.Infrastructure.Persistence.Seed;
using PlateBack.Infrastructure.Storage;

using Scalar.AspNetCore;

using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}"));

// ---------- Settings (checked at startup, so a missing secret fails fast) ----------
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<BusinessRules>().Bind(builder.Configuration.GetSection("BusinessRules"));

// ---------- Layers ----------
builder.Services.TryAddTimeProvider();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ---------- HTTP ----------
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .WithHeaders("Authorization", "Content-Type", CorrelationIdMiddleware.Header)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
    .AllowCredentials()));
builder.Services.AddOutputCache(o =>
    // No locking: with it, requests waiting for the first one share its result,
    // so one browser cancelling its request failed the others with a 500.
    o.AddPolicy(CachePolicies.Impact, p => p.Expire(TimeSpan.FromMinutes(5)).SetLocking(false)));
builder.Services.AddPlateBackOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddSingleton<ILiveUpdates, SignalRLiveUpdates>();

// ---------- Authentication and authorization ----------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>, TimeProvider>((options, jwt, time) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt.Value.Key),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            NameClaimType = "name",
            RoleClaimType = "role",
            // Expiry is checked against the app's TimeProvider (not the machine
            // clock), so tests can move time forward and see TOKEN_EXPIRED.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                var skew = TimeSpan.FromSeconds(30);
                if (expires is { } exp && now > exp + skew)
                {
                    throw new SecurityTokenExpiredException("The token has expired.") { Expires = exp };
                }
                if (notBefore is { } nbf && now < nbf - skew)
                {
                    throw new SecurityTokenNotYetValidException("The token is not valid yet.") { NotBefore = nbf };
                }
                return true;
            },
        };
        options.Events = new JwtBearerEvents
        {
            // Browsers cannot set headers on a WebSocket, so the SignalR client
            // sends the token in the query string; accept it on the hub only.
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments(LiveHub.Path)
                    && context.Request.Query["access_token"] is [{ Length: > 0 } token])
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
            // 401s and 403s use the same ProblemDetails shape as every other error.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.AuthenticateFailure is SecurityTokenExpiredException)
                {
                    await Problems.WriteAsync(context.HttpContext, 401, ErrorCodes.TokenExpired, "Token expired",
                        "The access token has expired. Call /auth/refresh for a new one.");
                    return;
                }
                await Problems.WriteAsync(context.HttpContext, 401, ErrorCodes.Unauthenticated, "Not signed in",
                    "Log in to continue.");
            },
            OnForbidden = context => Problems.WriteAsync(context.HttpContext, 403, ErrorCodes.Forbidden, "Forbidden",
                "Your account cannot do this."),
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthPolicies.Admin, p => p.RequireRole("Admin"));

var authPerMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 20);
var pickupCodesPerMinute = builder.Configuration.GetValue("RateLimits:PickupCodesPerMinute", 30);
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(RateLimits.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy(RateLimits.PickupCodes, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = pickupCodesPerMinute, Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Problems.WriteAsync(context.HttpContext, 429, ErrorCodes.RateLimited, "Too many requests",
            "Too many attempts. Wait a minute and try again.");
    };
});

var app = builder.Build();

// `dotnet run -- seed`: apply migrations and reset the demo data, then exit.
if (args.Contains("seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAsync();
    Log.Information("Database migrated and demo data seeded");
    return;
}

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // Never log here: the request log below writes each failure once, with its
    // trace id and stack trace. Logging here too gave every 500 a second copy.
    SuppressDiagnosticsCallback = _ => true,
});
app.UseStatusCodePages(async context =>
{
    // Unknown routes and wrong methods also answer with ProblemDetails.
    var http = context.HttpContext;
    if (http.Response.HasStarted || http.Response.ContentLength > 0 || http.Response.ContentType is not null)
    {
        return;
    }
    var status = http.Response.StatusCode;
    await Problems.WriteAsync(http, status, status == 404 ? ErrorCodes.NotFound : ErrorCodes.ValidationError,
        status == 404 ? "Not found" : "Request not supported",
        status == 404 ? "There is no endpoint at this address." : null);
});
app.UseMiddleware<CorrelationIdMiddleware>();
app.Use((context, next) =>
{
    // Browsers must not guess content types (uploaded files) or frame the API.
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    return next(context);
});
// The one log entry per request. It sees exceptions before ApiExceptionHandler
// turns them into answers, so expected ones (sold out, a bad code) are logged
// as Information and only unexpected ones as Error, with the stack trace.
app.UseSerilogRequestLogging(o => o.GetLevel = (http, _, exception) =>
    exception is not null ? (IsExpected(http, exception) ? LogEventLevel.Information : LogEventLevel.Error)
    : http.Response.StatusCode >= 500 ? LogEventLevel.Error
    : LogEventLevel.Information);
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

var uploads = app.Services.GetRequiredService<IOptions<UploadOptions>>().Value;
var uploadFolder = LocalImageStorage.FolderPath(uploads, app.Environment);
Directory.CreateDirectory(uploadFolder);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadFolder),
    RequestPath = uploads.RequestPath,
    ServeUnknownFileTypes = false,
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOutputCache();

app.MapOpenApi();
app.MapScalarApiReference(o => o.WithTitle("PlateBack API"));

app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "Healthy", database = "Connected" })
            : Results.Json(new { status = "Unhealthy", database = "Unreachable" }, statusCode: 503))
    .WithTags("Health")
    .WithSummary("Liveness and database check");

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapOrganizationEndpoints();
api.MapListingEndpoints();
api.MapReservationEndpoints();
api.MapNotificationEndpoints();
api.MapAdminEndpoints();
api.MapUploadEndpoints();
app.MapHub<LiveHub>(LiveHub.Path);

app.Run();

// Normal answers (validation, not found, sold out...) and requests the browser
// cancelled are not server errors, so they are not logged as errors.
static bool IsExpected(HttpContext http, Exception exception) =>
    exception is AppException or DomainException or DbUpdateConcurrencyException
        or UniqueConstraintException or BadHttpRequestException
    || (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested);

/// <summary>Entry point; public so the integration tests can host the API in memory.</summary>
public partial class Program;

internal static class TimeProviderRegistration
{
    /// <summary>The real clock, unless a test registered a fake one first.</summary>
    public static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(d => d.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
