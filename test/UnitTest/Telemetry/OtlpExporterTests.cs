#nullable disable

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OpenTelemetry.Trace;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Telemetry;

public class OtlpExporterTests(TestApplicationFactory<InstancesController> factory)
    : IClassFixture<TestApplicationFactory<InstancesController>>
{
    private const string TracesPath = "/v1/traces";

    [Fact]
    public async Task OtlpEndpointSet_ExportsTracesWithServiceNameFromEnvironment()
    {
        // Arrange
        using FakeOtlpCollector collector = FakeOtlpCollector.Start();
        using WebApplicationFactory<InstancesController> app = CreateApp(builder =>
        {
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", collector.Endpoint);
            builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
            builder.UseSetting("OTEL_SERVICE_NAME", "storage-otlp-test");
        });
        using HttpClient client = app.CreateClient();

        // Act
        await client.GetAsync("/storage/api/v1/otlp-exporter-test");
        byte[] export = await WaitForExport(app, collector);

        // Assert
        Assert.NotNull(export);
        string payload = Encoding.UTF8.GetString(export);
        Assert.Contains("storage-otlp-test", payload);
        Assert.DoesNotContain("platform-storage", payload);
    }

    [Fact]
    public async Task OtlpEndpointNotSet_ExportsNothingOverOtlp()
    {
        // Arrange
        using FakeOtlpCollector collector = FakeOtlpCollector.Start();
        using WebApplicationFactory<InstancesController> app = CreateApp(builder =>
        {
            // Would receive the traces if the OTLP exporter were registered without
            // OTEL_EXPORTER_OTLP_ENDPOINT.
            builder.UseSetting(
                "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
                collector.Endpoint + TracesPath
            );
            builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
        });
        using HttpClient client = app.CreateClient();

        // Act
        await client.GetAsync("/storage/api/v1/otlp-exporter-test");
        byte[] export = await WaitForExport(app, collector, TimeSpan.FromSeconds(2));

        // Assert
        Assert.Null(export);
    }

    private static async Task<byte[]> WaitForExport(
        WebApplicationFactory<InstancesController> app,
        FakeOtlpCollector collector,
        TimeSpan? timeout = null
    )
    {
        TracerProvider tracerProvider = app.Services.GetRequiredService<TracerProvider>();
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < (timeout ?? TimeSpan.FromSeconds(10)))
        {
            tracerProvider.ForceFlush();
            if (collector.TryTake(TracesPath, out byte[] body))
            {
                return body;
            }

            await Task.Delay(100);
        }

        return null;
    }

    private WebApplicationFactory<InstancesController> CreateApp(Action<IWebHostBuilder> configure)
    {
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new();
        Mock<IMessageBus> busMock = new();

        return factory.WithWebHostBuilder(builder =>
        {
            configure(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddMockRepositories();
                services.AddSingleton(keyVaultWrapper.Object);
                services.AddSingleton<
                    IPartiesWithInstancesClient,
                    PartiesWithInstancesClientMock
                >();
                services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                services.AddSingleton<
                    IPostConfigureOptions<JwtCookieOptions>,
                    JwtCookiePostConfigureOptionsStub
                >();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
                services.AddSingleton(busMock.Object);
            });
        });
    }

    /// <summary>
    /// Accepts OTLP/HTTP exports on a free local port and keeps the request bodies per path.
    /// </summary>
    private sealed class FakeOtlpCollector : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly ConcurrentQueue<(string Path, byte[] Body)> _requests = new();

        private FakeOtlpCollector(HttpListener listener, string endpoint)
        {
            _listener = listener;
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public static FakeOtlpCollector Start()
        {
            using TcpListener portFinder = new(IPAddress.Loopback, 0);
            portFinder.Start();
            int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
            portFinder.Stop();

            string endpoint = $"http://127.0.0.1:{port}";
            HttpListener listener = new();
            listener.Prefixes.Add(endpoint + "/");
            listener.Start();

            FakeOtlpCollector collector = new(listener, endpoint);
            _ = collector.ReceiveAsync();
            return collector;
        }

        public bool TryTake(string path, out byte[] body)
        {
            while (_requests.TryDequeue(out var request))
            {
                if (request.Path == path)
                {
                    body = request.Body;
                    return true;
                }
            }

            body = null;
            return false;
        }

        public void Dispose() => _listener.Close();

        private async Task ReceiveAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                using MemoryStream body = new();
                await context.Request.InputStream.CopyToAsync(body);
                _requests.Enqueue((context.Request.Url.AbsolutePath, body.ToArray()));

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/x-protobuf";
                context.Response.Close();
            }
        }
    }
}
