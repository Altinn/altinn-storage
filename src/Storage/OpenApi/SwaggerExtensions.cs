using System;
using System.Collections.Generic;
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
    /// <summary>
    /// The name of the public swagger document for the Storage API (filtered out internal apis)
    /// </summary>
    public const string V1PublicSwaggerDocName = "v1-public";

    /// <summary>
    /// The name of the swagger document for the Storage API as it is exposed through API Management
    /// </summary>
    public const string ApimSwaggerDocName = "v1-apim";

    /// <summary>
    /// The name of the complete swagger document for the Storage API (includes all apis)
    /// </summary>
    public const string CompleteSwaggerDocName = "v1";

    // The "env" segment is substituted per environment. The url omits the storage base path
    // because API Management exposes that as its own api suffix.
    private const string _apimGatewayUrl = "https://platform.env.altinn.cloud";

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
            ApimSwaggerDocName,
            new OpenApiInfo
            {
                Title = "Altinn Platform Storage - API Management",
                Version = "v1",
                Description =
                    "This is the API for Altinn Platform Storage as it is exposed through API Management. It contains every Storage endpoint, including the ones that are only reachable through API Management.",
            }
        );
        c.SwaggerDoc(
            CompleteSwaggerDocName,
            new OpenApiInfo { Title = "Altinn Platform Storage - complete", Version = "v1" }
        );
        c.AddServer(new() { Url = "https://platform.tt02.altinn.no", Description = "T02" });
        c.AddServer(new() { Url = "https://platform.altinn.no", Description = "Production" });

        c.DocInclusionPredicate(
            (docName, apiDesc) =>
                docName switch
                {
                    V1PublicSwaggerDocName => IncludeInPublicDoc(apiDesc.CustomAttributes()),
                    _ => true,
                }
        );
        c.AddDocumentFilterInstance(new MoveBasePathToServerSection(V1PublicSwaggerDocName));
        c.AddDocumentFilterInstance(
            new MoveBasePathToServerSection(
                ApimSwaggerDocName,
                new OpenApiServer { Url = _apimGatewayUrl }
            )
        );

        c.AddSecurityDefinition(
            JwtCookieDefaults.AuthenticationScheme,
            new OpenApiSecurityScheme
            {
                Description =
                    "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\". Remember to add \"Bearer\" to the input below before your token.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
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
            $"/swagger/{ApimSwaggerDocName}/swagger.json",
            "Altinn Storage API Management API"
        );
        options.SwaggerEndpoint(
            $"/swagger/{CompleteSwaggerDocName}/swagger.json",
            "Altinn Storage Complete API"
        );
    }

    private static bool IncludeInPublicDoc(IEnumerable<object> attributes)
    {
        return !attributes.Any(attr =>
            attr is ExcludeFromPublicStorageApiAttribute
            || attr
                is AuthorizeAttribute
                {
                    Policy: AuthzConstants.POLICY_STUDIO_DESIGNER
                        or AuthzConstants.POLICY_CORRESPONDENCE_SBLBRIDGE
                        or AuthzConstants.POLICY_SCOPE_APPDEPLOY
                }
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
