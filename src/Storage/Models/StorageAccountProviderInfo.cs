using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    /// fail to deserialize, and a document with a property the provider does not have fails as well.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
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
    /// The document is not valid for the provider, for example a required property is missing, an
    /// unknown property is present or a value is not allowed.
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
        where T : StorageAccountProviderInfo
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new JsonException("The provider info document is null.");
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }
}

/// <summary>
/// A container in an Azure Blob Storage account. The document names only the account and the
/// container; the blob repository derives the location from them, so a document cannot point the
/// repository, or its credentials, at any other host.
/// </summary>
/// <param name="AccountName">
/// The storage account name: 3 to 24 lowercase letters and digits, as Azure requires.
/// </param>
/// <param name="Container">
/// The container name: 3 to 63 lowercase letters, digits and hyphens, starting and ending with a
/// letter or digit and without consecutive hyphens, as Azure requires.
/// </param>
/// <exception cref="ArgumentException">A name is not a valid Azure name.</exception>
public sealed partial record AzureBlobProviderInfo(string AccountName, string Container)
    : StorageAccountProviderInfo
{
    /// <summary>
    /// Gets the storage account name.
    /// </summary>
    public string AccountName { get; } =
        AccountName is not null && AccountNamePattern().IsMatch(AccountName)
            ? AccountName
            : throw new ArgumentException(
                $"'{AccountName}' is not a valid Azure storage account name.",
                nameof(AccountName)
            );

    /// <summary>
    /// Gets the container name.
    /// </summary>
    public string Container { get; } =
        Container is { Length: >= 3 and <= 63 } && ContainerPattern().IsMatch(Container)
            ? Container
            : throw new ArgumentException(
                $"'{Container}' is not a valid Azure blob container name.",
                nameof(Container)
            );

    /// <inheritdoc/>
    [JsonIgnore]
    public override string Provider => AzureBlob;

    [GeneratedRegex(@"^[a-z0-9]{3,24}\z")]
    private static partial Regex AccountNamePattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex ContainerPattern();
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
