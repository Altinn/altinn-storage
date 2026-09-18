using System;
using Altinn.Platform.Storage.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class InstanceContinuationTokenTests
{
    [Fact]
    public void TryParse_RoundTripsSerializedToken()
    {
        // Arrange
        InstanceContinuationToken original = new(
            new DateTime(2024, 1, 1, 12, 30, 0, DateTimeKind.Utc),
            4711
        );

        // Act
        bool parsed = InstanceContinuationToken.TryParse(
            original.ToString(),
            out InstanceContinuationToken result
        );

        // Assert
        Assert.True(parsed);
        Assert.Equal(original, result);
        Assert.Equal(DateTimeKind.Utc, result.Timestamp.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("638400000000000000")]
    [InlineData("638400000000000000;")]
    [InlineData("638400000000000000;12;3")]
    [InlineData("not-a-number;12")]
    [InlineData("638400000000000000;not-a-number")]
    [InlineData("-1;12")]
    [InlineData("9223372036854775807;12")]
    public void TryParse_RejectsMalformedTokens(string? value)
    {
        // Act
        bool parsed = InstanceContinuationToken.TryParse(
            value,
            out InstanceContinuationToken result
        );

        // Assert
        Assert.False(parsed);
        Assert.Equal(default, result);
    }
}
