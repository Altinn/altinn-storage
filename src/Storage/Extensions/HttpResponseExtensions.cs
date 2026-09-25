#nullable disable

using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Sets the response headers that are common to the endpoints that serve data element content.
/// </summary>
public static class HttpResponseExtensions
{
    /// <summary>
    /// Tells the client to show the content and not to download it.
    /// </summary>
    /// <param name="response">The response to write the header to.</param>
    /// <param name="filename">The filename to present the content under.</param>
    public static void SetInlineContentDisposition(this HttpResponse response, string filename)
    {
        ContentDispositionHeaderValue contentDispositionHeader = new("inline");
        contentDispositionHeader.SetHttpFileName(filename);
        response.Headers.Append(
            HeaderNames.ContentDisposition,
            contentDispositionHeader.ToString()
        );
    }

    /// <summary>
    /// Adds an ETag header that identifies the blob version of the content. If the data element
    /// has no blob version, the method does not add the header. On-demand content has no blob
    /// version.
    /// </summary>
    /// <param name="response">The response to write the header to.</param>
    /// <param name="blobVersionId">The blob version id of the content being served.</param>
    public static void SetBlobVersionETag(this HttpResponse response, string blobVersionId)
    {
        string etag = BlobVersionId.ToETag(blobVersionId);
        if (etag is null)
        {
            return;
        }

        response.Headers[HeaderNames.ETag] = etag;
    }
}
