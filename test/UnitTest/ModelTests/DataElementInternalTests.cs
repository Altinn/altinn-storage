#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class DataElementInternalTests
{
    [Fact]
    public void Constructor_WithDataElementAndBlobVersionId_SetsProperties()
    {
        // Arrange
        DataElement dataElement = new()
        {
            BlobStoragePath = "ttd/some-app/instance-guid/data/data-guid",
        };

        // Act
        DataElementInternal dataElementInternal = new(dataElement, "blob-version-id");

        // Assert
        Assert.Same(dataElement, dataElementInternal.DataElement);
        Assert.Equal("blob-version-id", dataElementInternal.BlobVersionId);
    }

    [Fact]
    public void Constructor_WithNullDataElement_ThrowsArgumentNullException()
    {
        // Act
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            new DataElementInternal(null, null)
        );

        // Assert
        Assert.Equal("dataElement", exception.ParamName);
    }
}
