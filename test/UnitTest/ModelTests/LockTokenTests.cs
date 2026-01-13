using System;
using System.Buffers.Text;
using System.Text;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Models;
using VerifyXunit;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.ModelTests;

public class LockTokenTests
{
    private readonly Random _random = new();

    [Fact]
    public void LockToken_PropertiesAreSetCorrectly()
    {
        // Arrange
        var expectedId = 12345;
        var expectedSecret = new byte[20];
        _random.NextBytes(expectedSecret);

        // Act
        var lockToken = new LockToken(expectedId, expectedSecret);

        // Assert
        Assert.Equal(expectedId, lockToken.Id);
        Assert.Equal(expectedSecret, lockToken.Secret);
    }

    [Fact]
    public void CreateToken_ValidIdAndSecret_ReturnsBase64UrlEncodedToken()
    {
        // Arrange
        var id = 12345;
        var secret = new byte[20];
        _random.NextBytes(secret);
        var lockToken = new LockToken(id, secret);

        // Act
        var token = lockToken.CreateToken();
        var isValidBase64Url = Base64Url.IsValid(token);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.True(isValidBase64Url);
    }

    [Fact]
    public void ParseToken_ValidToken_ReturnsLockTokenWithCorrectValues()
    {
        // Arrange
        var expectedId = 12345;
        var expectedSecret = new byte[20];
        _random.NextBytes(expectedSecret);
        var originalToken = new LockToken(expectedId, expectedSecret);
        var tokenString = originalToken.CreateToken();

        // Act
        var parsedToken = LockToken.ParseToken(tokenString);

        // Assert
        Assert.Equal(expectedId, parsedToken.Id);
        Assert.Equal(expectedSecret, parsedToken.Secret);
    }

    [Fact]
    public async Task ParseToken_InvalidBase64Url_ThrowsFormatException()
    {
        // Arrange
        string invalidToken = "!!!invalid-base64!!!";

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => LockToken.ParseToken(invalidToken));
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_ValidBase64ButInvalidJson_ThrowsFormatException()
    {
        // Arrange
        var invalidJsonToken = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("invalid-json"));

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() =>
            LockToken.ParseToken(invalidJsonToken)
        );
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_JsonMissingId_ThrowsFormatException()
    {
        // Arrange
        var secret = new byte[20];
        _random.NextBytes(secret);
        var secretBase64 = Convert.ToBase64String(secret);
        var missingIdToken = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(
                $$"""
                {"secret":"{{secretBase64}}"}
                """
            )
        );

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => LockToken.ParseToken(missingIdToken));
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_JsonMissingSecret_ThrowsNullReferenceException()
    {
        // Arrange
        var missingSecretToken = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(
                """
                {"id":12345}
                """
            )
        );

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() =>
            LockToken.ParseToken(missingSecretToken)
        );
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_JsonWithEmptySecret_ThrowsFormatException()
    {
        // Arrange
        var emptySecretToken = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(
                """
                {"id":12345,"secret":""}
                """
            )
        );

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() =>
            LockToken.ParseToken(emptySecretToken)
        );
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_JsonWithInvalidBase64Secret_ThrowsFormatException()
    {
        // Arrange
        var invalidSecretToken = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(
                """
                {"id":12345,"secret":"!!!invalid-base64!!!"}
                """
            )
        );

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() =>
            LockToken.ParseToken(invalidSecretToken)
        );
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_JsonWithZeroId_ThrowsFormatException()
    {
        // Arrange
        var secret = new byte[20];
        _random.NextBytes(secret);
        var secretBase64 = Convert.ToBase64String(secret);
        var zeroIdToken = Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes(
                $$"""
                {"id":0,"secret":"{{secretBase64}}"}
                """
            )
        );

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => LockToken.ParseToken(zeroIdToken));
        await Verifier.Verify(new { Exception = exception });
    }

    [Fact]
    public async Task ParseToken_EmptyString_ThrowsFormatException()
    {
        // Arrange
        string emptyToken = "";

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => LockToken.ParseToken(emptyToken));
        await Verifier.Verify(new { Exception = exception });
    }
}
