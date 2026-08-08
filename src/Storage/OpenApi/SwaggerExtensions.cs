using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Altinn.Platform.Storage.Helpers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace Altinn.Platform.Storage.OpenApi;

/// <summary>
/// Extension methods for configuring Swagger for the Storage API
/// </summary>
public static class SwaggerExtensions
{
    public const string V1PublicSwaggerDocName = "v1-public";
    public const string CompleteSwaggerDocName = "v1";

    /// <summary>
    /// Configures Swagger for the Storage API. Pass this to services.AddSwaggerGen() in Program.cs.
    /// </summary>
    /// <param name="c">The SwaggerGenOptions to configure.</param>
    public static void StorageSwaggerGen(SwaggerGenOptions c)
    {
        c.SwaggerDoc(
            V1PublicSwaggerDocName,
            new OpenApiInfo
            {
                Title = "Altinn Platform Storage",
                Version = "v1",
                Description =
                    "This is the public API for Altinn Platform Storage. Note that the mutating endpoints (everything except GET) needs a platform access token and external users will need to call the api in the specific app",
            }
        );
        c.SwaggerDoc(
            CompleteSwaggerDocName,
            new OpenApiInfo { Title = "Altinn Platform Storage - complete", Version = "v1" }
        );
        c.AddServer(
            new() { Url = "https://platform.tt02.altinn.no/storage/api/v1", Description = "T02" }
        );
        c.AddServer(
            new() { Url = "https://platform.altinn.no/storage/api/v1", Description = "Production" }
        );

        // Exclude endpoints from the public openapi doc if they have the ExcludeFromPublicStorageApi attribute or if they have the Authorize attribute with the POLICY_STUDIO_DESIGNER or POLICY_CORRESPONDENCE_SBLBRIDGE policy
        c.DocInclusionPredicate(
            (docName, apiDesc) =>
            {
                if (docName != V1PublicSwaggerDocName)
                {
                    return true;
                }

                var attributes = apiDesc.CustomAttributes().ToList();

                // Exclude endpoints that have the ExcludeFromPublicStorageApi attribute
                if (attributes.Any(attr => attr is ExcludeFromPublicStorageApi))
                {
                    return false;
                }

                // Exclude endpoints that have the Authorize attribute with the POLICY_STUDIO_DESIGNER or POLICY_CORRESPONDENCE_SBLBRIDGE policy
                if (
                    attributes.Any(attr =>
                        attr
                            is AuthorizeAttribute
                            {
                                Policy: AuthzConstants.POLICY_STUDIO_DESIGNER
                                    or AuthzConstants.POLICY_CORRESPONDENCE_SBLBRIDGE
                            }
                    )
                )
                {
                    return false;
                }
                return true;
            }
        );
        c.AddDocumentFilterInstance(new RemoveStorageBasePathFilter(V1PublicSwaggerDocName));

        c.AddSecurityDefinition(
            JwtCookieDefaults.AuthenticationScheme,
            new OpenApiSecurityScheme
            {
                Description =
                    "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\". Remember to add \"Bearer\" to the input below before your token.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
            }
        );
        c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference(
                    JwtCookieDefaults.AuthenticationScheme,
                    document
                ),
                []
            },
        });
        try
        {
            c.IncludeXmlComments(GetXmlCommentsPathForControllers());

            // hardcoded since nuget restore does not export the xml file.
            c.IncludeXmlComments("Altinn.Platform.Storage.Interface.xml");
        }
        catch
        {
            // catch swashbuckle exception if it doesn't find the generated xml documentation file
        }
    }

    /// <summary>
    /// Configures the Swagger UI for the Storage API. Pass this to app.UseSwaggerUI() in Program.cs.
    /// </summary>
    /// <param name="options"></param>
    public static void ConfigureSwaggerUI(SwaggerUIOptions options)
    {
        options.SwaggerEndpoint(
            $"/swagger/{V1PublicSwaggerDocName}/swagger.json",
            "Altinn Storage Public API"
        );
        options.SwaggerEndpoint(
            $"/swagger/{CompleteSwaggerDocName}/swagger.json",
            "Altinn Storage Complete API"
        );
    }

    private static string GetXmlCommentsPathForControllers()
    {
        // locate the xml file being generated by .NET
        string xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        string xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

        return xmlPath;
    }
}
