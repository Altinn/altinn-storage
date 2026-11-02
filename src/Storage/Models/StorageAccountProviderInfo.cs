using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Provider-specific description of where a storage account keeps its blobs, stored as a JSON
/// document discriminated by the <c>provider</c> property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "provider")]
[JsonDerivedType(typeof(AzureBlobProviderInfo), "azure-blob")]
[JsonDerivedType(typeof(FileSystemProviderInfo), "file-system")]
public abstract record StorageAccountProviderInfo
{
    /// <summary>
    /// Gets the serializer options for provider info documents, shared by the database column and
    /// the file scan queue message.
    /// </summary>
    /// <remarks>
    /// PostgreSQL <c>jsonb</c> does not preserve key order, so the discriminator may come after
    /// other properties when the document is read back.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowOutOfOrderMetadataProperties = true,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
        };
}

/// <summary>
/// A container in an Azure Blob Storage account.
/// </summary>
/// <param name="AccountName">The storage account name.</param>
/// <param name="Container">The container name.</param>
/// <param name="Endpoint">
/// The blob service endpoint, for Azurite or non-public clouds. When null the public Azure endpoint
/// of the account is used.
/// </param>
public sealed record AzureBlobProviderInfo(
    string AccountName,
    string Container,
    Uri? Endpoint = null
) : StorageAccountProviderInfo
{
    /// <summary>
    /// Gets the blob service URI of the account.
    /// </summary>
    [JsonIgnore]
    public Uri ServiceUri => Endpoint ?? new Uri($"https://{AccountName}.blob.core.windows.net");

    /// <summary>
    /// Gets the URI of the container.
    /// </summary>
    [JsonIgnore]
    public Uri ContainerUri => new($"{ServiceUri.AbsoluteUri.TrimEnd('/')}/{Container}");
}

/// <summary>
/// A directory on a file system.
/// </summary>
/// <param name="Directory">The directory holding the blobs.</param>
public sealed record FileSystemProviderInfo(string Directory) : StorageAccountProviderInfo;
