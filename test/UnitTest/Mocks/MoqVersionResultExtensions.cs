#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Extensions;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Services;
using Moq.Language.Flow;

namespace Moq;

internal static class MoqVersionResultExtensions
{
    public static IReturnsResult<TMock> ReturnsAsync<TMock>(
        this ISetup<TMock, Task<DataElementWriteResult>> setup,
        DataElement value
    )
        where TMock : class =>
        setup.Returns(
            Task.FromResult(
                new DataElementWriteResult(value.FromApiModel(), new StorageVersions(1, 1))
            )
        );

    public static IReturnsResult<TMock> ReturnsAsync<TMock>(
        this ISetup<TMock, Task<DataElementWriteResult>> setup,
        DataElementInternal value
    )
        where TMock : class =>
        setup.Returns(
            Task.FromResult(new DataElementWriteResult(value, new StorageVersions(1, 1)))
        );

    public static IReturnsResult<TMock> ReturnsAsync<TMock>(
        this ISetup<TMock, Task<DataElementWriteResult>> setup,
        Func<DataElementInternal, long, CancellationToken, DataElementInternal> value
    )
        where TMock : class =>
        setup.Returns(
            (
                DataElementInternal dataElement,
                long instanceInternalId,
                int? expectedInstanceVersion,
                int? expectedProcessStateVersion,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new DataElementWriteResult(
                        value(dataElement, instanceInternalId, cancellationToken),
                        new StorageVersions(1, 1)
                    )
                )
        );

    public static IReturnsResult<TMock> ReturnsAsync<TMock>(
        this ISetup<TMock, Task<DataElementWriteResult>> setup,
        Func<
            Guid,
            Guid,
            Dictionary<string, object>,
            DataElementUpdateContext,
            CancellationToken,
            DataElement
        > value
    )
        where TMock : class =>
        setup.Returns(
            (
                Guid instanceGuid,
                Guid dataElementId,
                Dictionary<string, object> properties,
                DataElementUpdateContext context,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new DataElementWriteResult(
                        value(instanceGuid, dataElementId, properties, context, cancellationToken)
                            .FromApiModel(),
                        new StorageVersions(1, 1)
                    )
                )
        );

    public static IReturnsResult<TMock> ReturnsAsync<TMock>(
        this ISetup<TMock, Task<DataElementWriteResult>> setup,
        Func<Guid, Guid, FileScanStatus, CancellationToken, DataElement> value
    )
        where TMock : class =>
        setup.Returns(
            (
                Guid instanceGuid,
                Guid dataElementId,
                FileScanStatus fileScanStatus,
                CancellationToken cancellationToken
            ) =>
                Task.FromResult(
                    new DataElementWriteResult(
                        value(instanceGuid, dataElementId, fileScanStatus, cancellationToken)
                            .FromApiModel(),
                        new StorageVersions(1, 1)
                    )
                )
        );

    public static IReturnsResult<TMock> ReturnsAsync<TMock>(
        this ISetup<TMock, Task<DataUploadResult>> setup,
        Func<
            InstanceInternal,
            Stream,
            DataElementCreateOptions,
            long,
            int?,
            CancellationToken,
            (DataElementInternal DataElement, DateTimeOffset BlobTimestamp)
        > value
    )
        where TMock : class =>
        setup.Returns(
            (
                InstanceInternal instanceInternal,
                Stream stream,
                DataElementCreateOptions options,
                long instanceInternalId,
                int? storageAccountNumber,
                int? expectedInstanceVersion,
                int? expectedProcessStateVersion,
                CancellationToken cancellationToken
            ) =>
            {
                (DataElementInternal dataElement, DateTimeOffset blobTimestamp) = value(
                    instanceInternal,
                    stream,
                    options,
                    instanceInternalId,
                    storageAccountNumber,
                    cancellationToken
                );
                return Task.FromResult(
                    new DataUploadResult(dataElement, blobTimestamp, new StorageVersions(1, 1))
                );
            }
        );
}
