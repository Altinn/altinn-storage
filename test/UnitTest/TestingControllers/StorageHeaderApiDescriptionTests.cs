using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// The storage request headers are bound as action parameters so they are self-documenting and
/// appear in the generated API description. Reading them off <c>Request.Headers</c> instead would
/// leave them undocumented, which these tests exist to prevent.
/// </summary>
public class StorageHeaderApiDescriptionTests(TestApplicationFactory<ProcessController> factory)
    : IClassFixture<TestApplicationFactory<ProcessController>>
{
    public static TheoryData<string> RequestHeaders =>
        [
            StorageHeaders.IfInstanceVersionMatch,
            StorageHeaders.IfProcessStateVersionMatch,
            StorageHeaders.IdempotencyKey,
        ];

    [Theory]
    [MemberData(nameof(RequestHeaders))]
    public void StorageRequestHeader_IsDescribedOnEveryActionThatBindsIt(string headerName)
    {
        List<ApiDescription> descriptions = GetApiDescriptions();

        int describedActions = descriptions
            .Where(description =>
                description.ParameterDescriptions.Any(parameter => parameter.Name == headerName)
            )
            .Select(description => description.ActionDescriptor.Id)
            .Distinct()
            .Count();

        int bindingActions = CountActionsBinding(headerName);
        Assert.NotEqual(0, bindingActions);
        Assert.Equal(bindingActions, describedActions);
    }

    [Theory]
    [MemberData(nameof(RequestHeaders))]
    public void StorageRequestHeader_IsAnOptionalStringHeaderParameter(string headerName)
    {
        List<ApiParameterDescription> parameters =
        [
            .. GetApiDescriptions()
                .SelectMany(description => description.ParameterDescriptions)
                .Where(parameter => parameter.Name == headerName),
        ];

        Assert.NotEmpty(parameters);
        Assert.All(parameters, parameter => Assert.Equal(BindingSource.Header, parameter.Source));
        Assert.All(parameters, parameter => Assert.False(parameter.IsRequired));
        Assert.All(parameters, parameter => Assert.Equal(typeof(string), parameter.Type));
    }

    private static int CountActionsBinding(string headerName)
    {
        return typeof(ProcessController)
            .Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type =>
                type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            .Count(method =>
                method
                    .GetParameters()
                    .Any(parameter =>
                        parameter.GetCustomAttribute<FromHeaderAttribute>()?.Name == headerName
                    )
            );
    }

    private List<ApiDescription> GetApiDescriptions()
    {
        WebApplicationFactory<ProcessController> webApplicationFactory = factory.WithWebHostBuilder(
            builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                    .Build();
                builder.ConfigureAppConfiguration(
                    (hostingContext, config) => config.AddConfiguration(configuration)
                );
                builder.ConfigureTestServices(services => services.AddMockRepositories());
            }
        );
        webApplicationFactory.CreateClient().Dispose();

        return
        [
            .. webApplicationFactory
                .Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items.SelectMany(group => group.Items),
        ];
    }
}
