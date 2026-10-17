using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PlateBack.Api.Extensions;

/// <summary>Title, description and the Bearer security scheme for the OpenAPI document.</summary>
internal static class OpenApiSetup
{
    public static IServiceCollection AddPlateBackOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "PlateBack API",
                    Version = "v1",
                    Description =
                        "Reserve surplus food from local businesses and collect it with a pickup code. " +
                        "All endpoints are under /api/v1. Errors are RFC 9457 ProblemDetails with a machine-readable `code`. " +
                        "Sign in with POST /auth/login and send the access token as `Authorization: Bearer <token>`.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        Description = "Access token from /auth/login, /auth/register or /auth/refresh (valid 15 minutes).",
                    },
                };
                return Task.CompletedTask;
            });

            // Mark endpoints that need a token, so the docs page shows a lock and sends the header.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var needsAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
                if (needsAuth)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [],
                    });
                }
                return Task.CompletedTask;
            });
        });
}
