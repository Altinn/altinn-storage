using System;
using System.Buffers.Text;

namespace Altinn.Platform.Storage.Models;

internal static class BlobVersionId
{
    public static string Encode(Guid version)
    {
        return Base64Url.EncodeToString(version.ToByteArray(bigEndian: true));
    }

    public static Guid Decode(string versionId)
    {
        if (string.IsNullOrEmpty(versionId))
        {
            throw new ArgumentException("Blob version id cannot be empty.", nameof(versionId));
        }

        if (versionId.Length != 22)
        {
            throw new FormatException("Blob version id must be 22 characters.");
        }

        Span<byte> bytes = stackalloc byte[16];
        try
        {
            int bytesWritten = Base64Url.DecodeFromChars(versionId, bytes);
            if (bytesWritten != bytes.Length)
            {
                throw new FormatException("Blob version id decoded to an invalid length.");
            }
        }
        catch (FormatException exception)
        {
            throw new FormatException("Invalid blob version id.", exception);
        }

        return new Guid(bytes, bigEndian: true);
    }
}
