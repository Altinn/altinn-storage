#nullable disable

using Altinn.Platform.Storage.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Altinn.Platform.Storage.Extensions;

/// <summary>
/// Response headers shared by the endpoints that serve data element content.
/// </summary>
public static class HttpResponseExtensions
{
    /// <summary>
    /// Asks the client to render the content rather than offer it as a download.
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
    /// Tags the response with the blob version the content was read from. Does nothing when the
    /// data element carries no blob version, as on-demand generated content does.
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
