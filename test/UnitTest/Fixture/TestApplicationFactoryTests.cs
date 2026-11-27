using System;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Fixture;

public class TestApplicationFactoryTests
{
    [Fact]
    public async Task Dispose_AfterRequests_ReleasesTheHost()
    {
        // The first host in the process starts process-wide background work, such as the
        // OpenTelemetry self-diagnostics refresher, which keeps that host reachable.
        await StartUseAndDisposeHost();
        WeakReference services = await StartUseAndDisposeHost();

        for (int attempt = 0; attempt < 20 && services.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(100);
        }

        Assert.False(services.IsAlive, "The disposed host is still reachable.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> StartUseAndDisposeHost()
    {
        TestApplicationFactory<InstancesController> root = new();
        WebApplicationFactory<InstancesController> factory = root.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(Mock.Of<IMessageBus>()))
        );

        using (HttpClient client = factory.CreateClient())
        {
            HttpResponseMessage response = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        factory.Services.GetRequiredService<IRegisterService>();
        WeakReference services = new(factory.Services);
        await root.DisposeAsync();
        return services;
    }
}
