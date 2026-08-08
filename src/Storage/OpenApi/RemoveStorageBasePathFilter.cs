using System;
using Microsoft.OpenApi;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// This class is used to remove the storageBasePath parameter from the swagger documentation
/// </summary>
public class RemoveStorageBasePathFilter : Swashbuckle.AspNetCore.SwaggerGen.IDocumentFilter
{
    /// <inheritdoc/>
    public void Apply(
        OpenApiDocument document,
        Swashbuckle.AspNetCore.SwaggerGen.DocumentFilterContext context
    )
    {
        const string prefix = "/storage/api/v1";

        OpenApiPaths rewrittenPaths = new OpenApiPaths();

        foreach (var (path, pathItem) in document.Paths)
        {
            string rewrittenPath = path.StartsWith(prefix, StringComparison.Ordinal)
                ? path[prefix.Length..]
                : path;

            rewrittenPaths[rewrittenPath] = pathItem;
        }

        document.Paths = rewrittenPaths;
    }
}
