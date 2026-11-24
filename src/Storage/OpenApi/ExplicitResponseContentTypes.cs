using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Platform.Storage.OpenApi;

/// <summary>
/// Documents the content types a <see cref="ProducesResponseTypeAttribute"/> names for its status code,
/// such as <c>application/problem+json</c> for a problem. API Explorer gives every response the content
/// types of the action's <c>[Produces]</c> instead.
/// </summary>
internal sealed class ExplicitResponseContentTypes : IOperationFilter
{
    /// <inheritdoc/>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is null)
        {
            return;
        }

        foreach (
            IApiResponseMetadataProvider responseType in context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ProducesResponseTypeAttribute>()
        )
        {
            MediaTypeCollection contentTypes = [];
            responseType.SetContentTypes(contentTypes);
            if (
                contentTypes.Count == 0
                || !operation.Responses.TryGetValue(
                    responseType.StatusCode.ToString(),
                    out IOpenApiResponse? openApiResponse
                )
                || openApiResponse is not OpenApiResponse { Content.Count: > 0 } response
            )
            {
                continue;
            }

            OpenApiMediaType mediaType = response.Content.Values.First();
            response.Content = contentTypes.ToDictionary(
                contentType => contentType,
                _ => new OpenApiMediaType { Schema = mediaType.Schema }
            );
        }
    }
}
