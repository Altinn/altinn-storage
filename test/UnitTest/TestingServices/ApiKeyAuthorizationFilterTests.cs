using System.Collections.Generic;
using Altinn.Platform.Storage.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

/// <summary>
/// Tests the API key validation shared by <see cref="ApiKeyAuthorizationFilter"/> subclasses,
/// exercised through <see cref="CleanupApiKeyFilter"/>.
/// </summary>
public class ApiKeyAuthorizationFilterTests
{
    private const string ConfigurationKey = "StorageCleanupApiKey";
    private const string ValidApiKey = "a-valid-cleanup-key";

    [Fact]
    public void OnAuthorization_ValidApiKey_DoesNotSetResult()
    {
        // Arrange
        AuthorizationFilterContext context = CreateContext(ValidApiKey);
        CleanupApiKeyFilter sut = CreateSut(ValidApiKey);

        // Act
        sut.OnAuthorization(context);

        // Assert
        Assert.Null(context.Result);
    }

    [Fact]
    public void OnAuthorization_MissingApiKeyHeader_ReturnsUnauthorized()
    {
        // Arrange
        AuthorizationFilterContext context = CreateContext(apiKey: null);
        CleanupApiKeyFilter sut = CreateSut(ValidApiKey);

        // Act
        sut.OnAuthorization(context);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(context.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OnAuthorization_EmptyApiKey_ReturnsUnauthorized(string apiKey)
    {
        // Arrange
        AuthorizationFilterContext context = CreateContext(apiKey);
        CleanupApiKeyFilter sut = CreateSut(ValidApiKey);

        // Act
        sut.OnAuthorization(context);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(context.Result);
    }

    [Fact]
    public void OnAuthorization_InvalidApiKey_ReturnsUnauthorized()
    {
        // Arrange
        AuthorizationFilterContext context = CreateContext("not-the-configured-key");
        CleanupApiKeyFilter sut = CreateSut(ValidApiKey);

        // Act
        sut.OnAuthorization(context);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(context.Result);
    }

    [Fact]
    public void OnAuthorization_ApiKeyNotConfigured_ReturnsServiceUnavailable()
    {
        // Arrange
        AuthorizationFilterContext context = CreateContext(ValidApiKey);
        CleanupApiKeyFilter sut = CreateSut(configuredApiKey: null);

        // Act
        sut.OnAuthorization(context);

        // Assert
        StatusCodeResult result = Assert.IsType<StatusCodeResult>(context.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
    }

    private static CleanupApiKeyFilter CreateSut(string? configuredApiKey)
    {
        Dictionary<string, string?> settings = [];
        if (configuredApiKey is not null)
        {
            settings[ConfigurationKey] = configuredApiKey;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new CleanupApiKeyFilter(configuration, NullLogger<CleanupApiKeyFilter>.Instance);
    }

    private static AuthorizationFilterContext CreateContext(string? apiKey)
    {
        DefaultHttpContext httpContext = new();
        if (apiKey is not null)
        {
            httpContext.Request.Headers["X-API-Key"] = apiKey;
        }

        ActionContext actionContext = new(httpContext, new RouteData(), new ActionDescriptor());

        return new AuthorizationFilterContext(actionContext, []);
    }
}
