using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using OpenTelemetry.Logs;

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
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
// Serilog is the only logger. Deployed, each event is one JSON line (App
// Service's log stream and log search can filter on level and CorrelationId),
// and it is also handed to Application Insights when that is configured.
builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, logger) =>
{
    logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
    if (builder.Environment.IsProduction())
    {
        logger.WriteTo.Console(new RenderedCompactJsonFormatter());
    }
    else
    {
        logger.WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {CorrelationId}{NewLine}{Exception}");
    }
}, writeToProviders: true);

// ---------- Monitoring: Application Insights (Week 6) ----------
// On when App Service has APPLICATIONINSIGHTS_CONNECTION_STRING: requests,
// SQL and Blob Storage calls, exceptions and the logs above, all linked by the
// request's trace id.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        // Added before the exporter, so it runs first.
        .WithLogging(logging => logging.AddProcessor(new ExpectedErrorsProcessor()))
        .UseAzureMonitor();
}

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

// ---------- Behind a proxy (Azure App Service) ----------
// App Service's front end receives the HTTPS request and passes it on over
// HTTP, adding X-Forwarded-For (the visitor's IP) and X-Forwarded-Proto
// (https). Only trusted where the app cannot be reached any other way
// (ForwardedHeaders:Enabled, on in production): anywhere else a visitor
// could send these headers themselves.
var trustForwardedHeaders = builder.Configuration.GetValue("ForwardedHeaders:Enabled", false);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The front end's addresses are not published, so no allow-list; instead
    // only the last entry is used, the one the front end itself appended.
    // An address a visitor puts in the header is to the left of it and ignored.
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
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

// Deployed (Database:MigrateOnStartup, Database:SeedDemoIfEmpty): bring the
// schema up to date before serving requests, and give a brand-new database the
// demo data. Unlike `seed`, this never deletes anything, so restarts and
// redeploys keep every account and reservation.
var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (database.MigrateOnStartup || database.SeedDemoIfEmpty)
{
    await using var scope = app.Services.CreateAsyncScope();
    if (database.MigrateOnStartup)
    {
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        Log.Information("Database migrations applied");
    }
    if (database.SeedDemoIfEmpty)
    {
        var demo = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")
            && string.IsNullOrEmpty(demo.AdminPassword))
        {
            throw new InvalidOperationException(
                "Demo:AdminPassword must be set when demo data is seeded on a deployed site; " +
                "otherwise the demo admin keeps the published password.");
        }
        if (await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedIfEmptyAsync())
        {
            Log.Information("Demo data inserted into the empty database");
        }
    }
}

// First, so everything after it (request log, HTTPS redirection, rate
// limiter) sees the visitor's IP and https, not the front end's.
if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
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

// ---------- Listing photos ----------
var uploads = app.Services.GetRequiredService<IOptions<UploadOptions>>().Value;
var storage = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
if (!storage.UsesBlob)
{
    var uploadFolder = LocalImageStorage.FolderPath(uploads, app.Environment);
    Directory.CreateDirectory(uploadFolder);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadFolder),
        RequestPath = uploads.RequestPath,
        ServeUnknownFileTypes = false,
    });
}

// ---------- The React app (Week 6) ----------
// The deploy workflow puts the frontend's build into wwwroot, so the browser
// gets the app, the API and the SignalR hub from one address, as with the
// Week 4 dev proxy: no CORS, and the refresh cookie stays SameSite=Strict.
// Without a wwwroot (development, tests) none of this is active.
var hasFrontend = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
var contentSecurityPolicy = string.Join("; ",
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'", // React style attributes (stock bars, chart)
    "img-src 'self' data: blob:", // data: and blob: for the photo preview before upload
    "font-src 'self'",
    $"connect-src 'self' {builder.Configuration["ContentSecurityPolicy:ConnectSources"]}".TrimEnd(),
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'");
var frontendFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var headers = context.Context.Response.Headers;
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
        {
            // Vite puts a content hash in these names: a new release means new names.
            headers.CacheControl = "public, max-age=31536000, immutable";
        }
        else
        {
            // index.html and friends: always ask, so a new release is picked up at once.
            headers.CacheControl = "no-cache";
            headers.ContentSecurityPolicy = contentSecurityPolicy;
        }
    },
};
if (hasFrontend)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(frontendFiles);
}

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

if (storage.UsesBlob)
{
    // Photos stream from the private container under the same /uploads address.
    app.MapGet($"{uploads.RequestPath.TrimEnd('/')}/{{fileName}}",
            async (string fileName, BlobImageStorage photos, HttpResponse response, CancellationToken ct) =>
            {
                var image = await photos.OpenAsync(fileName, ct);
                if (image is null)
                {
                    return Results.NotFound();
                }
                response.Headers.CacheControl = "public, max-age=31536000, immutable"; // names are random and never reused
                return Results.Stream(image.Content, image.ContentType);
            })
        .ExcludeFromDescription();
}

if (hasFrontend)
{
    // Any other path without a file extension is a page of the React app
    // (/browse, /me/reservations...), so a refresh or a shared link works.
    // Files (nonfile) are left to the static file middleware, and API, hub,
    // health and docs paths keep their ProblemDetails 404.
    app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|hubs/|health$|openapi/|scalar).*$)}", "index.html", frontendFiles);
}

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
