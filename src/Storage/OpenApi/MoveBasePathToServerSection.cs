using System;
using System.Linq;
using Microsoft.OpenApi;

namespace Altinn.Platform.Storage.OpenApi;

/// <summary>
/// This class is used to remove the storageBasePath prefix from the swagger documentation
/// </summary>
public class MoveBasePathToServerSection : Swashbuckle.AspNetCore.SwaggerGen.IDocumentFilter
{
    private const string _prefix = "/storage/api/v1";

    private readonly string _documentName;
    private readonly OpenApiServer? _gatewayServer;

    /// <summary>
    /// Appends the base path to the servers already documented for the swagger document.
    /// </summary>
    public MoveBasePathToServerSection(string documentName)
    {
        _documentName = documentName;
    }

    /// <summary>
    /// Documents a single gateway server, url unchanged, for gateways that add the base path themselves.
    /// </summary>
    public MoveBasePathToServerSection(string documentName, OpenApiServer gatewayServer)
    {
        _documentName = documentName;
        _gatewayServer = gatewayServer;
    }

    /// <inheritdoc/>
    public void Apply(
        OpenApiDocument swaggerDoc,
        Swashbuckle.AspNetCore.SwaggerGen.DocumentFilterContext context
    )
    {
        if (string.Equals(context.DocumentName, _documentName, StringComparison.Ordinal))
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

    private void RewriteServers(OpenApiDocument swaggerDoc)
    {
        if (_gatewayServer is not null)
        {
            swaggerDoc.Servers = [_gatewayServer];
            return;
        }

        if (swaggerDoc.Servers is null)
        {
            return;
        }

        swaggerDoc.Servers = swaggerDoc
            .Servers.Select(server => new OpenApiServer
            {
                Url = $"{server.Url}{_prefix}",
                Description = server.Description,
                Variables = server.Variables,
                Extensions = server.Extensions,
            })
            .ToList();
    }
}
