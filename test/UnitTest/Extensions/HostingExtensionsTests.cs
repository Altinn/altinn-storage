using System;
using System.Linq;
using Altinn.Platform.Storage.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

public class HostingExtensionsTests
{
    [Fact]
    public void UseGracefulShutdown_Production_ConfiguresHostLifetimeAndShutdownTimeout()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Production }
        );

        builder.UseGracefulShutdown();

        using ServiceProvider services = builder.Services.BuildServiceProvider();
        var hostLifetime = services.GetRequiredService<IHostLifetime>();
        var hostOptions = services.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal("AppHostLifetime", hostLifetime.GetType().Name);
        Assert.Equal(TimeSpan.FromSeconds(50), hostOptions.ShutdownTimeout);
    }

    [Fact]
    public void UseGracefulShutdown_Development_DoesNotReplaceHostLifetime()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );
        int registrationsBefore = builder.Services.Count(descriptor =>
            descriptor.ServiceType == typeof(IHostLifetime)
        );

        builder.UseGracefulShutdown();

        int registrationsAfter = builder.Services.Count(descriptor =>
            descriptor.ServiceType == typeof(IHostLifetime)
        );
        Assert.Equal(registrationsBefore, registrationsAfter);
    }
}
