#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class DataElementInternalTests
{
    [Fact]
    public void FromApiModel_CreatesStandaloneDomainValueAndKeepsStorageField()
    {
        // Arrange
        DataElement dataElement = new()
        {
            Id = "legacy-non-guid-data-element-id",
            InstanceGuid = "legacy-non-guid-instance-id",
            BlobStoragePath = "ttd/some-app/instance-guid/data/data-guid",
            Metadata = [new() { Key = "shared", Value = "value" }],
        };

        // Act
        DataElementInternal dataElementInternal = dataElement.FromApiModel();

        // Assert
        Assert.Equal(dataElement.Id, dataElementInternal.Id);
        Assert.Equal(dataElement.InstanceGuid, dataElementInternal.InstanceGuid);
        Assert.Equal(dataElement.BlobStoragePath, dataElementInternal.BlobStoragePath);
        Assert.Same(dataElement.Metadata, dataElementInternal.Metadata);

        dataElement.Filename = "api-only.txt";
        Assert.Null(dataElementInternal.Filename);
    }

    [Fact]
    public void FromApiModel_WithNullDataElement_ThrowsArgumentNullException()
    {
        // Act
        DataElement dataElement = null;
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            dataElement.FromApiModel()
        );

        // Assert
        Assert.Equal("dataElement", exception.ParamName);
    }
}
