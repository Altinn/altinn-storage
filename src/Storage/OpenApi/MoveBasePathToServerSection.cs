using System;
using Microsoft.OpenApi;

namespace Altinn.Platform.Storage.OpenApi;

/// <summary>
/// This class is used to remove the storageBasePath prefix from the swagger documentation
/// </summary>
/// <param name="documentName">The name of the swagger document to apply this filter to</param>
public class MoveBasePathToServerSection(string documentName)
    : Swashbuckle.AspNetCore.SwaggerGen.IDocumentFilter
{
    private const string _prefix = "/storage/api/v1";

    /// <inheritdoc/>
    public void Apply(
        OpenApiDocument swaggerDoc,
        Swashbuckle.AspNetCore.SwaggerGen.DocumentFilterContext context
    )
    {
        if (context.DocumentName == documentName)
        {
            RewriteServers(swaggerDoc);
            RewritePath(swaggerDoc);
        }
    }

    private static void RewritePath(OpenApiDocument swaggerDoc)
    {
        OpenApiPaths rewrittenPaths = new OpenApiPaths();

        foreach (var (path, pathItem) in swaggerDoc.Paths)
        {
            string rewrittenPath = path.StartsWith(_prefix, StringComparison.Ordinal)
                ? path[_prefix.Length..]
                : path;

            rewrittenPaths[rewrittenPath] = pathItem;
        }

        swaggerDoc.Paths = rewrittenPaths;
    }

    private static void RewriteServers(OpenApiDocument swaggerDoc)
    {
        if (swaggerDoc.Servers is not null)
        {
            foreach (var server in swaggerDoc.Servers)
            {
                if (server.Url?.EndsWith(_prefix, StringComparison.Ordinal) != true)
                {
                    server.Url = $"{server.Url}{_prefix}";
                }
            }
        }
    }
}
