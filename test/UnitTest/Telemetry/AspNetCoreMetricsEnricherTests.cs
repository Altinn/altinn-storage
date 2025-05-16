using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Telemetry;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Clients;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Telemetry;

public class AspNetCoreMetricsEnricherTests(TestApplicationFactory<InstancesController> factory)
    : IClassFixture<TestApplicationFactory<InstancesController>>
{
    private readonly TestApplicationFactory<InstancesController> _factory = factory;
    
    [Fact]
    public async Task ActionsSnapshot()
    {
        using var server = StartServer();

        var descriptors = server.ActionDescriptorProvider;
        var snapshot = new 
        {
            ActionsToValidate = descriptors.ActionsToValidate.Select(a => a.DisplayName).Order().ToArray(),
            ScopesToValidate = descriptors.ActionsToValidate.Select(a => new 
            {
                Endpoint = a.DisplayName,
                Scopes = ((FrozenSet<string>)a.Properties[AspNetCoreMetricsEnricher.AllowedScopesKey])
                    .Order()
                    .ToArray(),
            }).ToImmutableSortedDictionary(a => a.Endpoint, a => a.Scopes),
            IgnoredActions = descriptors.ActionsNotValidated.Select(a => a.DisplayName).Order().ToArray(),
        };

        await VerifyXunit.Verifier.Verify(snapshot);
    }

    private sealed record Fixture(WebApplicationFactory<InstancesController> Factory, HttpClient HttpClient) : IDisposable
    {
        public CustomActionDescriptorProvider ActionDescriptorProvider => Factory.Services.GetRequiredService<CustomActionDescriptorProvider>();

        public void Dispose()
        {
            HttpClient.Dispose();
            Factory.Dispose();
        }
    }

    private Fixture StartServer()
    {
        // No setup required for these services. They are not in use by the InstanceController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            IConfiguration configuration = new ConfigurationBuilder().AddJsonFile(ServiceUtil.GetAppsettingsPath()).Build();
            builder.ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddConfiguration(configuration);
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddMockRepositories();

                services.AddSingleton(keyVaultWrapper.Object);

                services.AddSingleton<IPartiesWithInstancesClient, PartiesWithInstancesClientMock>();
                services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();
            });
        });

        return new Fixture(factory, factory.CreateClient());
    }
}
