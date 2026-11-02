using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// Provider-specific description of where a storage account keeps its blobs. The provider is stored
/// beside the document and selects the derived type; the document holds only that provider's own
/// properties.
/// </summary>
/// <remarks>
/// The document carries no type discriminator because PostgreSQL <c>jsonb</c> does not preserve key
/// order, so the type cannot depend on anything read from inside the document.
/// </remarks>
public abstract record StorageAccountProviderInfo
{
    /// <summary>
    /// The provider of <see cref="AzureBlobProviderInfo"/>.
    /// </summary>
    public const string AzureBlob = "azure-blob";

    /// <summary>
    /// The provider of <see cref="FileSystemProviderInfo"/>.
    /// </summary>
    public const string FileSystem = "file-system";

    /// <summary>
    /// Gets the serializer options for provider info documents.
    /// </summary>
    /// <remarks>
    /// The two Respect* flags make a document that lacks a required property, or has it set to null,
    /// fail to deserialize.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
        };

    /// <summary>
    /// Gets the provider, which selects the derived type a document is read as. Overrides are
    /// <see cref="JsonIgnoreAttribute"/> as well, since the serializer reads the attribute from the
    /// most derived declaration.
    /// </summary>
    [JsonIgnore]
    public abstract string Provider { get; }

    /// <summary>
    /// Reads a provider info document as the type the provider selects.
    /// </summary>
    /// <param name="provider">The provider, such as <see cref="AzureBlob"/>.</param>
    /// <param name="json">The provider info document.</param>
    /// <returns>The provider info.</returns>
    /// <exception cref="NotSupportedException">The provider is unknown.</exception>
    /// <exception cref="JsonException">
    /// The document is not valid for the provider, for example a required property is missing.
    /// </exception>
    public static StorageAccountProviderInfo Deserialize(string provider, string json) =>
        provider switch
        {
            AzureBlob => Deserialize<AzureBlobProviderInfo>(json),
            FileSystem => Deserialize<FileSystemProviderInfo>(json),
            _ => throw new NotSupportedException(
                $"Storage account provider '{provider}' is not supported."
            ),
        };

    /// <summary>
    /// Writes the provider info document, without the provider.
    /// </summary>
    /// <returns>The provider info document.</returns>
    public string Serialize() => JsonSerializer.Serialize(this, GetType(), JsonOptions);

    /// <summary>
    /// Writes the provider info document, without the provider, as a JSON element.
    /// </summary>
    /// <returns>The provider info document.</returns>
    public JsonElement SerializeToElement() =>
        JsonSerializer.SerializeToElement(this, GetType(), JsonOptions);

    private static T Deserialize<T>(string json)
        where T : StorageAccountProviderInfo =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new JsonException("The provider info document is null.");
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
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Provider => AzureBlob;

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
public sealed record FileSystemProviderInfo(string Directory) : StorageAccountProviderInfo
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Provider => FileSystem;
}
