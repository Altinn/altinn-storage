#nullable disable

using System;
using System.IO;
using System.Threading.Tasks;
using System.Web;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Models;
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
    public static string DataFileName(string appId, Guid instanceGuid, Guid dataElementId)
    {
        return $"{appId}/{instanceGuid}/data/{dataElementId}";
    }

    /// <summary>
    /// Formats a filename for a blob version of a data element.
    /// </summary>
    internal static string GetVersionedBlobPath(
        string appId,
        Guid instanceGuid,
        string blobVersionId
    )
    {
        return $"{VersionedBlobPathPrefix(appId, instanceGuid)}{blobVersionId}";
    }

    /// <summary>
    /// Throws an exception if the blob storage path isn't in the excpected format.
    /// </summary>
    public static void EnsureExpectedBlobStoragePath(
        DataElementInternal dataElement,
        Guid instanceGuid,
        string appId
    )
    {
        if (
            !IsExpectedBlobStoragePath(
                dataElement.BlobStoragePath,
                appId,
                instanceGuid,
                dataElement.Id
            )
        )
        {
            throw new InvalidOperationException(
                $"Blob storage path of data element {dataElement.Id} was unexpected for instance {instanceGuid}."
            );
        }
    }

    /// <summary>
    /// Makes sure that the blob storage path agrees with the requested instance and data element
    /// ids. Two paths are correct: the legacy path without a version, and the path of the current
    /// blob version of the element. Throws an exception for all other paths.
    /// </summary>
    internal static void EnsureBlobStoragePathMatchesRequest(
        DataElementInternal dataElement,
        string appId,
        Guid instanceGuid,
        Guid dataGuid
    )
    {
        if (!BlobStoragePathMatchesRequest(dataElement, appId, instanceGuid, dataGuid))
        {
            throw new InvalidOperationException(
                $"Blob storage path of data element {dataGuid} was unexpected for instance {instanceGuid}."
            );
        }
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

    internal static bool IsExpectedBlobStoragePath(
        string blobStoragePath,
        string appId,
        Guid instanceGuid,
        Guid dataElementId
    )
    {
        if (string.IsNullOrEmpty(blobStoragePath))
        {
            return false;
        }

        if (
            string.Equals(
                blobStoragePath,
                DataFileName(appId, instanceGuid, dataElementId),
                StringComparison.Ordinal
            )
        )
        {
            return true;
        }

        string versionedPathPrefix = VersionedBlobPathPrefix(appId, instanceGuid);
        if (!blobStoragePath.StartsWith(versionedPathPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> blobVersionId = blobStoragePath.AsSpan(versionedPathPrefix.Length);

        return blobVersionId.ContainsAnyExcept('.') && !blobVersionId.Contains('/');
    }

    private static bool BlobStoragePathMatchesRequest(
        DataElementInternal dataElement,
        string appId,
        Guid instanceGuid,
        Guid dataGuid
    )
    {
        string blobStoragePath = dataElement.BlobStoragePath;
        if (string.IsNullOrEmpty(blobStoragePath))
        {
            return false;
        }

        string legacyBlobStoragePath = DataFileName(appId, instanceGuid, dataGuid);
        if (string.Equals(blobStoragePath, legacyBlobStoragePath, StringComparison.Ordinal))
        {
            return true;
        }

        string blobVersionId = dataElement.BlobVersionId;
        if (string.IsNullOrEmpty(blobVersionId))
        {
            return false;
        }

        string versionedBlobStoragePath = GetVersionedBlobPath(appId, instanceGuid, blobVersionId);
        return string.Equals(blobStoragePath, versionedBlobStoragePath, StringComparison.Ordinal);
    }

    private static string VersionedBlobPathPrefix(string appId, Guid instanceGuid)
    {
        return $"{appId}/{instanceGuid}/data-elements/";
    }
}
