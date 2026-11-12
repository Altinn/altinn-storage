using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Azure.Storage;
using Azure.Storage.Blobs;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Utils;

/// <summary>
/// Checks every row in <c>storage.dataelementblobversions</c> of an org against blob storage: the blob of
/// the row is in the storage account the resolver gives for the row, and in none of the other containers
/// the caller knows about.
/// </summary>
/// <remarks>
/// Existence is checked with the Azure SDK and the shared key of the configured Azurite account, so only
/// Azure blob accounts in Azurite can be audited.
/// </remarks>
public static class BlobVersionAudit
{
    private const string _readRowsSql = """
        select id, instanceguid, dataelementid, appid, blobstorageorg, storageaccountnumber,
            storageaccountid, created, detachedat, datatype
        from storage.dataelementblobversions
        where blobstorageorg = $1
        order by created, id
        """;

    /// <summary>
    /// Audits the blob version rows of an org.
    /// </summary>
    /// <param name="dataSource">The database holding the rows.</param>
    /// <param name="resolver">The resolver that decides which storage account a row was written to.</param>
    /// <param name="storageConfiguration">The Azurite account whose shared key reads the containers.</param>
    /// <param name="org">The blob storage org of the rows.</param>
    /// <param name="otherContainers">
    /// Containers a blob must not be in unless the resolver names them for its row.
    /// </param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>One finding per row, in the order the rows were created.</returns>
    public static async Task<IReadOnlyList<Finding>> Run(
        NpgsqlDataSource dataSource,
        IStorageAccountResolver resolver,
        AzureStorageConfiguration storageConfiguration,
        string org,
        IEnumerable<Uri> otherContainers,
        CancellationToken cancellationToken = default
    )
    {
        StorageSharedKeyCredential credential = new(
            storageConfiguration.AccountName,
            storageConfiguration.AccountKey
        );
        List<Uri> knownContainers = [.. otherContainers.Distinct()];
        List<Finding> findings = [];
        foreach (DataElementBlobVersion row in await ReadRows(dataSource, org, cancellationToken))
        {
            StorageAccount account = await resolver.ForVersion(row, cancellationToken);
            if (account.ProviderInfo is not AzureBlobProviderInfo info)
            {
                throw new NotSupportedException(
                    $"Blob version {row.Id} is in a {account.ProviderInfo.Provider} account, which the audit cannot read."
                );
            }

            string blobStoragePath = DataElementHelper.GetVersionedBlobPath(
                row.AppId,
                row.InstanceGuid,
                BlobVersionId.Encode(row.Id)
            );
            Uri containerUri = AzureBlobLocation.Of(info, storageConfiguration).ContainerUri;
            bool present = await Exists(containerUri, blobStoragePath);

            List<Uri> alsoPresentIn = [];
            foreach (Uri container in knownContainers.Where(c => c != containerUri))
            {
                if (await Exists(container, blobStoragePath))
                {
                    alsoPresentIn.Add(container);
                }
            }

            findings.Add(
                new Finding(row, blobStoragePath, account, containerUri, present, alsoPresentIn)
            );
        }

        return findings;

        async Task<bool> Exists(Uri containerUri, string blobStoragePath)
        {
            BlobClient blob = new BlobContainerClient(
                containerUri,
                credential,
                TestContainers.AzuriteClientOptions
            ).GetBlobClient(blobStoragePath);
            return await blob.ExistsAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Asserts that every row has its blob in its own account and in no other known container.
    /// </summary>
    /// <param name="findings">The audit result.</param>
    public static void AssertConsistent(IReadOnlyList<Finding> findings)
    {
        Assert.All(
            findings,
            finding =>
            {
                Assert.True(
                    finding.Present,
                    $"Blob version {finding.Row.Id} is missing from {finding.Container}."
                );
                Assert.True(
                    finding.AlsoPresentIn.Count == 0,
                    $"Blob version {finding.Row.Id} is also in {string.Join(", ", finding.AlsoPresentIn)}."
                );
            }
        );
    }

    /// <summary>
    /// Asserts that no row has a blob in any known container.
    /// </summary>
    /// <param name="findings">The audit result.</param>
    public static void AssertNoBlobs(IReadOnlyList<Finding> findings)
    {
        Assert.All(
            findings,
            finding =>
            {
                Assert.False(
                    finding.Present,
                    $"Blob version {finding.Row.Id} is still in {finding.Container}."
                );
                Assert.Empty(finding.AlsoPresentIn);
            }
        );
    }

    private static async Task<List<DataElementBlobVersion>> ReadRows(
        NpgsqlDataSource dataSource,
        string org,
        CancellationToken cancellationToken
    )
    {
        List<DataElementBlobVersion> rows = [];
        await using NpgsqlCommand command = dataSource.CreateCommand(_readRowsSql);
        command.Parameters.AddWithValue(org);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(
                new DataElementBlobVersion
                {
                    Id = reader.GetGuid(0),
                    InstanceGuid = reader.GetGuid(1),
                    DataElementId = reader.GetGuid(2),
                    AppId = reader.GetString(3),
                    BlobStorageOrg = reader.GetString(4),
                    StorageAccountNumber = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    StorageAccountId = reader.IsDBNull(6) ? null : reader.GetInt16(6),
                    Created = reader.GetFieldValue<DateTimeOffset>(7),
                    DetachedAt = reader.IsDBNull(8)
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(8),
                    DataType = reader.IsDBNull(9) ? null : reader.GetString(9),
                }
            );
        }

        return rows;
    }

    /// <summary>
    /// The audit result of one blob version row.
    /// </summary>
    /// <param name="Row">The blob version row.</param>
    /// <param name="BlobStoragePath">The path of the row's blob.</param>
    /// <param name="Account">The storage account the resolver gives for the row.</param>
    /// <param name="Container">The container of that storage account.</param>
    /// <param name="Present">Whether the blob is in that account.</param>
    /// <param name="AlsoPresentIn">The other known containers holding a blob at the same path.</param>
    public sealed record Finding(
        DataElementBlobVersion Row,
        string BlobStoragePath,
        StorageAccount Account,
        Uri Container,
        bool Present,
        IReadOnlyList<Uri> AlsoPresentIn
    );
}
