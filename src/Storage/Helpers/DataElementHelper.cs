#nullable disable

using System.IO;
using System.Threading.Tasks;
using System.Web;
using Altinn.Platform.Storage.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Altinn.Platform.Storage.Helpers;

/// <summary>
/// DataElement helper methods
/// </summary>
public static class DataElementHelper
{
    /// <summary>
    /// Formats a filename for blob storage.
    /// </summary>
    public static string DataFileName(string appId, string instanceGuid, string dataElementId)
    {
        return $"{appId}/{instanceGuid}/data/{dataElementId}";
    }

    /// <summary>
    /// Get the stream from the request
    /// </summary>
    /// <param name="request">The request</param>
    /// <param name="limit">MultipartBoundaryLengthLimit</param>
    /// <returns></returns>
    public static async Task<(
        Stream Stream,
        string ContentType,
        string ContentFileName,
        long FileSize
    )> GetStream(HttpRequest request, int limit)
    {
        string contentType;
        string contentFileName = null;
        long fileSize = 0;
        Stream stream;
        if (MultipartRequestHelper.IsMultipartContentType(request.ContentType))
        {
            // Only read the first section of the Multipart message.
            MediaTypeHeaderValue mediaType = MediaTypeHeaderValue.Parse(request.ContentType);
            string boundary = MultipartRequestHelper.GetBoundary(mediaType, limit);

            MultipartReader reader = new(boundary, request.Body);
            MultipartSection section = await reader.ReadNextSectionAsync();

            stream = section.Body;
            contentType = section.ContentType;

            bool hasContentDisposition = ContentDispositionHeaderValue.TryParse(
                section.ContentDisposition,
                out ContentDispositionHeaderValue contentDisposition
            );

            if (hasContentDisposition)
            {
                contentFileName = HttpUtility.UrlDecode(contentDisposition.GetFilename());
                fileSize = contentDisposition.Size ?? 0;
            }
        }
        else
        {
            stream = request.Body;
            if (request.Headers.TryGetValue("Content-Disposition", out StringValues headerValues))
            {
                bool hasContentDisposition = ContentDispositionHeaderValue.TryParse(
                    headerValues.ToString(),
                    out ContentDispositionHeaderValue contentDisposition
                );

                if (hasContentDisposition)
                {
                    contentFileName = HttpUtility.UrlDecode(contentDisposition.GetFilename());
                    fileSize = contentDisposition.Size ?? 0;
                }
            }

            contentType = request.ContentType;
        }

        return (stream, contentType, contentFileName, fileSize);
    }
}
