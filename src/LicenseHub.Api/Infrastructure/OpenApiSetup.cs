using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

namespace LicenseHub.Api.Infrastructure;

public static class OpenApiSetup
{
    public const string BearerScheme = "Bearer";

    /// <summary>
    /// Adds the OpenAPI document with a Bearer (JWT) security scheme, required by every operation that needs
    /// authorization, so the admin routes can be tried from the Scalar UI after logging in.
    /// </summary>
    public static IServiceCollection AddLicenseHubOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Admin access token from POST /admin/auth/login (valid for 30 minutes).",
                };
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
                if (requiresAuth)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });
}
