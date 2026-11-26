using System;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Configuration;

public class GeneralSettingsValidationTests(TestApplicationFactory<InstancesController> factory)
    : IClassFixture<TestApplicationFactory<InstancesController>>
{
    [Theory]
    [InlineData("0")]
    [InlineData("3601")]
    public void FileScanReadUrlLifetimeOutOfRange_StorageDoesNotStart(string lifetimeSeconds)
    {
        // Arrange
        using WebApplicationFactory<InstancesController> app = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("GeneralSettings:FileScanReadUrlLifetimeSeconds", lifetimeSeconds)
        );

        // Act
        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
            app.CreateClient()
        );

        // Assert
        Assert.Equal(typeof(GeneralSettings), exception.OptionsType);
        Assert.Contains(
            nameof(GeneralSettings.FileScanReadUrlLifetimeSeconds),
            exception.Message,
            StringComparison.Ordinal
        );
    }
}
