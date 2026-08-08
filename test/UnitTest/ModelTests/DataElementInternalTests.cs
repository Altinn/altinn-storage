#nullable disable

using System;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class DataElementInternalTests
{
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
