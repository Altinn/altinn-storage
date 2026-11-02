using System.Collections.Generic;
using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices;

public class StorageAccountSettingsValidatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void Validate_PositiveCacheSeconds_Succeeds(int cacheSeconds)
    {
        ValidateOptionsResult result = new StorageAccountSettingsValidator().Validate(
            null,
            new StorageAccountSettings { CacheSeconds = cacheSeconds }
        );

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_CacheSecondsNotPositive_FailsWithMessage(int cacheSeconds)
    {
        ValidateOptionsResult result = new StorageAccountSettingsValidator().Validate(
            null,
            new StorageAccountSettings { CacheSeconds = cacheSeconds }
        );

        Assert.True(result.Failed);
        Assert.Equal(
            $"StorageAccounts:CacheSeconds must be greater than zero, but is {cacheSeconds}.",
            result.FailureMessage
        );
    }

    [Fact]
    public void Options_ZeroCacheSecondsInConfiguration_ThrowsOptionsValidationException()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["StorageAccounts:CacheSeconds"] = "0" }
            )
            .Build();
        ServiceCollection services = new();
        services
            .AddOptions<StorageAccountSettings>()
            .Bind(configuration.GetSection("StorageAccounts"));
        services.AddSingleton<
            IValidateOptions<StorageAccountSettings>,
            StorageAccountSettingsValidator
        >();
        using ServiceProvider provider = services.BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<StorageAccountSettings>>().Value
        );
        Assert.Contains(
            "StorageAccounts:CacheSeconds must be greater than zero",
            exception.Message
        );
    }
}
