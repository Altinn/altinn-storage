#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers;

/// <summary>
/// Represents a collection of integration tests of the <see cref="DataController"/>.
/// </summary>
public class DataControllerTests : IClassFixture<TestApplicationFactory<DataController>>
{
    private const string _versionPrefix = "/storage/api/v1";
    private TestTelemetry _testTelemetry;
    private readonly TestApplicationFactory<DataController> _factory;
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="DataControllerTests"/> class.
    /// </summary>
    /// <param name="factory">Platform storage fixture.</param>
    public DataControllerTests(TestApplicationFactory<DataController> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Scenario:
    ///   Add data element to created instances.
    /// Expected:
    ///   Request is authorized
    /// Success:
    /// Created
    /// </summary>
    [Theory]
    [InlineData("", 1L)]
    [InlineData(PrincipalUtil.AltinnPortalUserScope, null)]
    [InlineData("altinn:instances.write", null)]
    [InlineData("something", 1L)]
    public async Task Post_NewData_Ok(string scope, long? invalidScopeRequests)
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
        HttpContent content = new StringContent("This is a blob file");

        string token = PrincipalUtil.GetToken(1337, 1337, 3, scopes: [scope]);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.PostAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _testTelemetry.AssertRequestsWithInvalidScopesCountAsync(invalidScopeRequests);
    }

    [Fact]
    public async Task Post_NewDataThatRequiresFileScan_Ok()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
        HttpContent content = new StringContent("This is a blob file");

        Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(null, null, fileScanMock, token);

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{dataPathWithData}?dataType=default_with_fileScan",
            content
        );

        // Assert
        fileScanMock.Verify(
            f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once()
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        string responseContent = await response.Content.ReadAsStringAsync();
        DataElement actual = JsonSerializer.Deserialize<DataElement>(
            responseContent,
            _serializerOptions
        );

        Assert.Equal(FileScanResult.Pending, actual.FileScanResult);
    }

    /// <summary>
    /// Scenario:
    ///   Add data element to created instances. Authenticated users is not authorized to perform this operation.
    /// Expected:
    ///   Request is authorized
    /// Success:
    /// Created
    /// </summary>
    [Fact]
    public async Task Post_NewData_NotAuthorized()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/69c259d1-9c1f-4ab6-9d8b-5c210042dc4f/data";
        HttpContent content = new StringContent("This is a blob file");

        string token = PrincipalUtil.GetToken(1, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.PostAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   Add data element to created instances. Authenticated users is not authorized to perform this operation.
    /// Expected:
    ///   Request is authorized
    /// Success:
    /// Created
    /// </summary>
    [Fact]
    public async Task Post_NewData_ToLowAuthenticationLevel()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/69c259d1-9c1f-4ab6-9d8b-5c210042dc4f/data";
        HttpContent content = new StringContent("This is a blob file");

        string token = PrincipalUtil.GetToken(3, 1337, 0);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.PostAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(SensitiveDataApp.DataTypes.Default, AuthenticationType.Org, HttpStatusCode.Created)]
    [InlineData(
        SensitiveDataApp.DataTypes.Default,
        AuthenticationType.User,
        HttpStatusCode.Created
    )]
    [InlineData(
        SensitiveDataApp.DataTypes.SensitiveRead,
        AuthenticationType.Org,
        HttpStatusCode.Created
    )]
    [InlineData(
        SensitiveDataApp.DataTypes.SensitiveRead,
        AuthenticationType.User,
        HttpStatusCode.Created
    )]
    [InlineData(
        SensitiveDataApp.DataTypes.SensitiveWrite,
        AuthenticationType.Org,
        HttpStatusCode.Created
    )]
    [InlineData(
        SensitiveDataApp.DataTypes.SensitiveWrite,
        AuthenticationType.User,
        HttpStatusCode.Forbidden
    )]
    [InlineData(
        SensitiveDataApp.DataTypes.SensitiveBoth,
        AuthenticationType.Org,
        HttpStatusCode.Created
    )]
    [InlineData(
        SensitiveDataApp.DataTypes.SensitiveBoth,
        AuthenticationType.User,
        HttpStatusCode.Forbidden
    )]
    public async Task Post_DataElement_ValidatesDataTypeWriteAccess(
        string dataType,
        AuthenticationType authenticationType,
        HttpStatusCode expectedStatusCode
    )
    {
        // Arrange
        var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/data/?dataType={dataType}";
        var token =
            authenticationType is AuthenticationType.User
                ? PrincipalUtil.GetToken(1337, 1337, 3)
                : PrincipalUtil.GetOrgToken("ttd");
        var client = GetTestClient(bearerAuthToken: token);

        // Act
        var response = await client.PostAsync(dataPath, new StringContent("Blob content"));
        var content = async () =>
            JsonSerializer.Deserialize<DataElement>(
                await response.Content.ReadAsStringAsync(),
                _serializerOptions
            );

        // Assert
        Assert.Equal(expectedStatusCode, response.StatusCode);

        if (expectedStatusCode == HttpStatusCode.Created)
        {
            Assert.Equal(dataType, (await content()).DataType);
        }
        else
        {
            await Assert.ThrowsAsync<JsonException>(content);
        }
    }

    /// <summary>
    /// Scenario:
    ///   Add data element to created instances.
    /// Expected:
    ///   Request is authorized
    /// Success:
    /// Created
    /// </summary>
    [Fact]
    public async Task OverwriteData_UpdateData_Ok()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";
        HttpContent content = new StringContent("This is a blob file with updated data");

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);

        // Act
        HttpResponseMessage response = await client.PutAsync(dataPathWithData, content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string responseContent = await response.Content.ReadAsStringAsync();
        DataElement actual = JsonSerializer.Deserialize<DataElement>(
            responseContent,
            _serializerOptions
        );

        Assert.Equal(FileScanResult.NotApplicable, actual.FileScanResult);
    }

    [Fact]
    public async Task OverwriteData_UpdateDataOnDataTypeWithFileScan_StartsFileScan()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/50c60b30-cb9a-435b-a31e-bbce47c2b936";
        HttpContent content = new StringContent("This is a blob file with updated data");

        Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(null, null, fileScanMock, token);

        // Act
        HttpResponseMessage response = await client.PutAsync(dataPathWithData, content);

        // Assert
        fileScanMock.Verify(
            f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once()
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string responseContent = await response.Content.ReadAsStringAsync();
        DataElement actual = JsonSerializer.Deserialize<DataElement>(
            responseContent,
            _serializerOptions
        );

        Assert.Equal(FileScanResult.Pending, actual.FileScanResult);
    }

    [Fact]
    public async Task OverwriteData_DataElementDoesNotExist_ReturnsNotFound()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11111111-6681-47a1-9626-fcf6c27308a5";
        HttpContent content = new StringContent("This is a blob file with updated data");

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.PutAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   Add data element to created instances.
    /// Expected:
    ///   Request is authorized
    /// Success:
    ///   Created
    /// </summary>
    [Fact]
    public async Task OverwriteData_UpdateData_Conflict()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/6aa47207-f089-4c11-9cb2-f00af6f66a47/data/24bfec2e-c4ce-4e82-8fa9-aa39da329fd5";
        HttpContent content = new StringContent("This is a blob file with updated data");

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.PutAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(SensitiveDataApp.DataElements.Default, AuthenticationType.Org, HttpStatusCode.OK)]
    [InlineData(SensitiveDataApp.DataElements.Default, AuthenticationType.User, HttpStatusCode.OK)]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveRead,
        AuthenticationType.Org,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveRead,
        AuthenticationType.User,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveWrite,
        AuthenticationType.Org,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveWrite,
        AuthenticationType.User,
        HttpStatusCode.Forbidden
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveBoth,
        AuthenticationType.Org,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveBoth,
        AuthenticationType.User,
        HttpStatusCode.Forbidden
    )]
    public async Task OverwriteData_DataElement_ValidatesDataTypeWriteAccess(
        string dataElementId,
        AuthenticationType authenticationType,
        HttpStatusCode expectedStatusCode
    )
    {
        // Arrange
        var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/data/{dataElementId}";
        var token =
            authenticationType is AuthenticationType.User
                ? PrincipalUtil.GetToken(1337, 1337, 3)
                : PrincipalUtil.GetOrgToken("ttd");
        var client = GetTestClient(bearerAuthToken: token);

        // Act
        var response = await client.PutAsync(dataPath, new StringContent("Blob content"));

        // Assert
        Assert.Equal(expectedStatusCode, response.StatusCode);
    }

    [Fact]
    public async Task Delete_DataElement_Ok()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_DataElementDoesNotExist_ReturnsNotFound()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11111111-6681-47a1-9626-fcf6c27308a5";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_DataElement_NotAuthorized()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";

        string token = PrincipalUtil.GetToken(1, 1, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(SensitiveDataApp.DataElements.Default, AuthenticationType.Org, HttpStatusCode.OK)]
    [InlineData(SensitiveDataApp.DataElements.Default, AuthenticationType.User, HttpStatusCode.OK)]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveRead,
        AuthenticationType.Org,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveRead,
        AuthenticationType.User,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveWrite,
        AuthenticationType.Org,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveWrite,
        AuthenticationType.User,
        HttpStatusCode.Forbidden
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveBoth,
        AuthenticationType.Org,
        HttpStatusCode.OK
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveBoth,
        AuthenticationType.User,
        HttpStatusCode.Forbidden
    )]
    public async Task Delete_DataElement_ValidatesDataTypeWriteAccess(
        string dataElementId,
        AuthenticationType authenticationType,
        HttpStatusCode expectedStatusCode
    )
    {
        // Arrange
        var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/data/{dataElementId}";
        var token =
            authenticationType is AuthenticationType.User
                ? PrincipalUtil.GetToken(1337, 1337, 3)
                : PrincipalUtil.GetOrgToken("ttd");
        var client = GetTestClient(bearerAuthToken: token);

        // Act
        var response = await client.DeleteAsync(dataPath);

        // Assert
        Assert.Equal(expectedStatusCode, response.StatusCode);
    }

    [Theory]
    [InlineData("", 1L)]
    [InlineData(PrincipalUtil.AltinnPortalUserScope, null)]
    [InlineData("altinn:instances.read", null)]
    [InlineData("something", 1L)]
    public async Task Get_DataElement_Ok(string scope, long? invalidScopeRequests)
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

        string token = PrincipalUtil.GetToken(1337, 1337, 3, scopes: [scope]);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _testTelemetry.AssertRequestsWithInvalidScopesCountAsync(invalidScopeRequests);
    }

    [Fact]
    public async Task Get_DataElementStoredAtVersionedBlobStoragePath_ReturnsContent()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/{VersionedBlobElement.InstanceGuid}/data/{VersionedBlobElement.DataElementId}";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);

        // Act
        using HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        // Assert
        await VerifyXunit.Verifier.Verify(new { Response = response });
    }

    [Fact]
    public async Task OverwriteData_DataElementStoredAtVersionedBlobStoragePath_Ok()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/{VersionedBlobElement.InstanceGuid}/data/{VersionedBlobElement.DataElementId}";
        HttpContent content = new StringContent("This is a blob file with updated data");

        Mock<IBlobRepository> blobRepositoryMock = new();
        blobRepositoryMock
            .Setup(b =>
                b.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.Is<string>(blobStoragePath =>
                        blobStoragePath.StartsWith(
                            $"tdd/endring-av-navn/{VersionedBlobElement.InstanceGuid}/data-elements/"
                        )
                        && blobStoragePath != VersionedBlobElement.BlobStoragePath
                    ),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((37, DateTimeOffset.UtcNow))
            .Verifiable();

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(
            blobRepositoryMock: blobRepositoryMock,
            bearerAuthToken: token
        );

        // Act
        HttpResponseMessage response = await client.PutAsync(dataPathWithData, content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        blobRepositoryMock.Verify();
    }

    [Fact]
    public async Task Get_DataElementDoesNotExists_ReturnsNotFound()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/11111111-8eed-4d1d-9d75-9239c40724e9";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElements_Ok()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElementsAsEndUser_HardDeletedFilteredOut()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/dataelements/";
        int expectedCount = 2;

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);
        string content = await response.Content.ReadAsStringAsync();
        DataElementList actual = JsonSerializer.Deserialize<DataElementList>(
            content,
            _serializerOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCount, actual.DataElements.Count);
    }

    [Fact]
    public async Task Get_DataElementsAsAppOwner_HardDeletedIncluded()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/dataelements/";
        int expectedCount = 3;

        string token = PrincipalUtil.GetOrgToken("ttd");
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);
        string content = await response.Content.ReadAsStringAsync();
        DataElementList actual = JsonSerializer.Deserialize<DataElementList>(
            content,
            _serializerOptions
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCount, actual.DataElements.Count);
    }

    [Fact]
    public async Task Get_DataElements_To_Low_Auth_Level()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

        string token = PrincipalUtil.GetToken(1337, 1337, 1);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElements_NotAuthorized()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

        string token = PrincipalUtil.GetToken(1, 1, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElements_ReturnsAll_RegardlessOfDataTypeReadRestriction()
    {
        // Arrange
        var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/dataelements";
        var orgClient = GetTestClient(bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"));
        var userClient = GetTestClient(bearerAuthToken: PrincipalUtil.GetToken(1337, 1337, 3));

        List<string> expectedDataElementTypes =
        [
            SensitiveDataApp.DataTypes.Default,
            SensitiveDataApp.DataTypes.SensitiveRead,
            SensitiveDataApp.DataTypes.SensitiveWrite,
            SensitiveDataApp.DataTypes.SensitiveBoth,
        ];

        // Act
        List<DataElementList> results = [];
        foreach (var client in new[] { orgClient, userClient })
        {
            var response = await client.GetAsync(dataPath);
            var content = JsonSerializer.Deserialize<DataElementList>(
                await response.Content.ReadAsStringAsync(),
                _serializerOptions
            );
            results.Add(content);
        }

        // Assert
        Assert.All(
            results,
            x => Assert.Equal(expectedDataElementTypes.Count, x.DataElements.Count)
        );
        Assert.All(
            results,
            x => Assert.Equivalent(expectedDataElementTypes, x.DataElements.Select(y => y.DataType))
        );
    }

    [Fact]
    public async Task Get_DataElement_NotAuthorized()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

        string token = PrincipalUtil.GetToken(1, 1, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElement_ToLowAuthenticationLevel()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

        string token = PrincipalUtil.GetToken(1337, 1337, 1);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElement_Org_Ok()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/ca9da17c-904a-44d2-9771-a5420acfbcf3/data/28023597-516b-4a71-a77c-d3736912abd5";

        string token = PrincipalUtil.GetOrgToken("tdd");
        HttpClient client = GetTestClient(bearerAuthToken: token);

        // Act
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElementAsEndUser_HardDeleted_NotFound()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_DataElementAsAppOwner_HardDeletedIncluded()
    {
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";

        string token = PrincipalUtil.GetOrgToken("ttd");
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage response = await client.GetAsync(dataPathWithData);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(
        SensitiveDataApp.DataElements.Default,
        AuthenticationType.Org,
        HttpStatusCode.OK,
        "model-content"
    )]
    [InlineData(
        SensitiveDataApp.DataElements.Default,
        AuthenticationType.User,
        HttpStatusCode.OK,
        "model-content"
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveRead,
        AuthenticationType.Org,
        HttpStatusCode.OK,
        "sensitive-data-read-content"
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveRead,
        AuthenticationType.User,
        HttpStatusCode.Forbidden,
        ""
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveWrite,
        AuthenticationType.Org,
        HttpStatusCode.OK,
        "sensitive-data-write-content"
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveWrite,
        AuthenticationType.User,
        HttpStatusCode.OK,
        "sensitive-data-write-content"
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveBoth,
        AuthenticationType.Org,
        HttpStatusCode.OK,
        "sensitive-data-both-content"
    )]
    [InlineData(
        SensitiveDataApp.DataElements.SensitiveBoth,
        AuthenticationType.User,
        HttpStatusCode.Forbidden,
        ""
    )]
    public async Task Get_DataElement_ValidatesDataTypeReadAccess(
        string dataElementId,
        AuthenticationType authenticationType,
        HttpStatusCode expectedStatusCode,
        string expectedContent
    )
    {
        // Arrange
        var dataPath = $"{SensitiveDataApp.GetInstanceUrl()}/data/{dataElementId}";
        var token =
            authenticationType is AuthenticationType.User
                ? PrincipalUtil.GetToken(1337, 1337, 3)
                : PrincipalUtil.GetOrgToken("ttd");
        var client = GetTestClient(bearerAuthToken: token);

        // Act
        var response = await client.GetAsync(dataPath);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(expectedStatusCode, response.StatusCode);
        Assert.Equal(expectedContent, content);
    }

    [Fact]
    public async Task Delete_Delayed_AutoDeleteMissing_BadRequest()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9?delay=true";
        string expected = "\"DataType default does not support delayed deletion\"";
        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);
        string actual = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("duplicate-updates")]
    [InlineData("duplicate-deletes")]
    [InlineData("update-and-delete")]
    public async Task CommitMutation_DuplicateDataElementMutationIds_ReturnsBadRequest(
        string requestShape
    )
    {
        // Arrange
        Guid dataElementId = Guid.Parse(SensitiveDataApp.DataElements.Default);
        InstanceMutationRequest request = requestShape switch
        {
            "duplicate-updates" => new InstanceMutationRequest
            {
                UpdateDataElements =
                [
                    new InstanceMutationUpdateDataElement
                    {
                        DataElementId = dataElementId,
                        Locked = false,
                    },
                    new InstanceMutationUpdateDataElement
                    {
                        DataElementId = dataElementId,
                        Locked = true,
                    },
                ],
            },
            "duplicate-deletes" => new InstanceMutationRequest
            {
                DeleteDataElements =
                [
                    new InstanceMutationDeleteDataElement { DataElementId = dataElementId },
                    new InstanceMutationDeleteDataElement { DataElementId = dataElementId },
                ],
            },
            "update-and-delete" => new InstanceMutationRequest
            {
                UpdateDataElements =
                [
                    new InstanceMutationUpdateDataElement
                    {
                        DataElementId = dataElementId,
                        Locked = true,
                    },
                ],
                DeleteDataElements =
                [
                    new InstanceMutationDeleteDataElement { DataElementId = dataElementId },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(requestShape)),
        };

        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock
        );

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );
        string content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ExpectedDuplicateDataElementMutationIdsResponse(dataElementId), content);
        InstanceMutationAsserts.VerifyApplyNever(mutationRepositoryMock);
    }

    [Fact]
    public async Task CommitMutation_RepositoryConflicts_OnlyProcessStatusUsesProblemDetailsResponse()
    {
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            status: null
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        mutationRepositoryMock
            .SetupSequence(repository =>
                repository.Apply(
                    Guid.Parse(SensitiveDataApp.InstanceGuid),
                    storedInstance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ThrowsAsync(new ProcessStatusConflictException(ProcessStatus.Processing))
            .ThrowsAsync(new RepositoryException("unrelated conflict", HttpStatusCode.Conflict));
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            DataValues = new Dictionary<string, string> { ["value"] = "updated" },
        };

        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument responseBody = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync()
        );
        Assert.Equal(
            "process_status_conflict",
            responseBody.RootElement.GetProperty("type").GetString()
        );
        Assert.Equal(
            (int)HttpStatusCode.Conflict,
            responseBody.RootElement.GetProperty("status").GetInt32()
        );
        Assert.Contains(
            ProcessStatus.Processing,
            responseBody.RootElement.GetProperty("detail").GetString(),
            StringComparison.Ordinal
        );

        HttpResponseMessage unrelatedResponse = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        Assert.Equal(HttpStatusCode.Conflict, unrelatedResponse.StatusCode);
        Assert.Equal("application/json", unrelatedResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"unrelated conflict\"", await unrelatedResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitMutation_NewlyEndedProcess_ArchivesResponseAndStoredInstance(
        bool withExistingStatus
    )
    {
        // Arrange
        DateTime ended = new(2026, 7, 10, 9, 8, 7, DateTimeKind.Utc);
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            withExistingStatus ? new InstanceStatus { ReadStatus = ReadStatus.Read } : null
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            ProcessState = new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    Started = storedInstance.Process.Started,
                    StartEvent = storedInstance.Process.StartEvent,
                    Ended = ended,
                    EndEvent = "EndEvent_1",
                },
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string responseBody = await response.Content.ReadAsStringAsync();
        if (!withExistingStatus)
        {
            Assert.Equal(
                """{"instance":{"id":"1337/99194777-a691-433a-ace1-225e9a691653","instanceOwner":{"partyId":"1337"},"appId":"ttd/sensitive-data","org":"ttd","selfLinks":{"platform":"https://platform.at22.altinn.cloud/storage/api/v1/instances/1337/99194777-a691-433a-ace1-225e9a691653"},"process":{"started":"2026-07-10T08:00:00Z","startEvent":"StartEvent_1","ended":"2026-07-10T09:08:07Z","endEvent":"EndEvent_1"},"status":{"isArchived":true,"archived":"2026-07-10T09:08:07Z","isSoftDeleted":false,"isHardDeleted":false,"readStatus":"Unread"},"data":[]},"createdDataElementIds":[],"replayed":false}""",
                responseBody
            );
        }

        using JsonDocument responseJson = JsonDocument.Parse(responseBody);
        JsonElement responseStatus = responseJson
            .RootElement.GetProperty("instance")
            .GetProperty("status");
        Assert.True(responseStatus.GetProperty("isArchived").GetBoolean());
        Assert.Equal(ended, responseStatus.GetProperty("archived").GetDateTime());
        Assert.True(storedInstance.Status.IsArchived);
        Assert.Equal(ended, storedInstance.Status.Archived);
        if (withExistingStatus)
        {
            Assert.Equal("Read", responseStatus.GetProperty("readStatus").GetString());
            Assert.Equal(ReadStatus.Read, storedInstance.Status.ReadStatus);
        }

        Assert.Equal(ended, storedInstance.Process.Ended);
        Assert.NotNull(capturedMutation);
        Assert.Equal(
            1,
            capturedMutation.InstanceUpdateProperties.Count(property =>
                property == nameof(InstanceInternal.Status)
            )
        );
        Assert.Contains(
            nameof(InstanceStatus.IsArchived),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.Contains(nameof(InstanceStatus.Archived), capturedMutation.InstanceUpdateProperties);
    }

    [Fact]
    public async Task CommitMutation_NonEndedProcess_DoesNotTouchStatus()
    {
        // Arrange
        InstanceStatus initialStatus = new() { ReadStatus = ReadStatus.Read };
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                CurrentTask = new ProcessElementInfo
                {
                    ElementId = "Task_1",
                    AltinnTaskType = "data",
                },
            },
            initialStatus
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            ProcessState = new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    Started = storedInstance.Process.Started,
                    StartEvent = storedInstance.Process.StartEvent,
                    CurrentTask = new ProcessElementInfo { ElementId = "Task_2" },
                },
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(initialStatus, storedInstance.Status);
        Assert.False(storedInstance.Status.IsArchived);
        Assert.Null(storedInstance.Status.Archived);
        Assert.NotNull(capturedMutation);
        Assert.Null(capturedMutation.InstanceUpdates.Status);
        Assert.DoesNotContain(
            nameof(InstanceInternal.Status),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.DoesNotContain(
            nameof(InstanceStatus.IsArchived),
            capturedMutation.InstanceUpdateProperties
        );
        Assert.DoesNotContain(
            nameof(InstanceStatus.Archived),
            capturedMutation.InstanceUpdateProperties
        );
    }

    [Fact]
    public async Task CommitMutation_AlreadyEndedStoredProcess_ReturnsForbidden()
    {
        // Arrange
        DateTime storedEnded = new(2026, 7, 10, 8, 30, 0, DateTimeKind.Utc);
        DateTime storedArchived = new(2026, 7, 10, 8, 31, 0, DateTimeKind.Utc);
        DateTime incomingEnded = new(2026, 7, 10, 9, 0, 0, DateTimeKind.Utc);
        InstanceStatus initialStatus = new()
        {
            IsArchived = true,
            Archived = storedArchived,
            ReadStatus = ReadStatus.Read,
        };
        InstanceInternal storedInstance = CreateMutationInstance(
            new ProcessState
            {
                Started = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
                StartEvent = "StartEvent_1",
                Ended = storedEnded,
                EndEvent = "EndEvent_1",
            },
            initialStatus
        );
        Mock<IInstanceRepository> instanceRepositoryMock = CreateMutationInstanceRepository(
            storedInstance
        );
        InstanceMutationCommit capturedMutation = null;
        Mock<IInstanceMutationRepository> mutationRepositoryMock =
            CreatePersistingMutationRepository(
                storedInstance,
                mutation => capturedMutation = mutation
            );
        HttpClient client = GetTestClient(
            bearerAuthToken: PrincipalUtil.GetOrgToken("ttd"),
            mutationRepositoryMock: mutationRepositoryMock,
            instanceRepositoryMock: instanceRepositoryMock
        );
        InstanceMutationRequest request = new()
        {
            ProcessState = new ProcessStateUpdate
            {
                State = new ProcessState
                {
                    Started = storedInstance.Process.Started,
                    StartEvent = storedInstance.Process.StartEvent,
                    Ended = incomingEnded,
                    EndEvent = "EndEvent_2",
                },
            },
        };

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{SensitiveDataApp.GetInstanceUrl()}/mutations",
            JsonContent.Create(request, options: _serializerOptions)
        );

        // Assert
        // An ended process has no current task, so process-state mutations are rejected
        // (parity with the process controller); the stored instance stays untouched.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Same(initialStatus, storedInstance.Status);
        Assert.True(storedInstance.Status.IsArchived);
        Assert.Equal(storedArchived, storedInstance.Status.Archived);
        Assert.Equal(storedEnded, storedInstance.Process.Ended);
        Assert.Null(capturedMutation);
    }

    [Fact]
    public async Task Delete_Delayed_AggregateMutationApplied()
    {
        // Arrange
        DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe30");
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        InstanceMutationCommit capturedMutation = null;
        dataRepositoryMock
            .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(de.FromApiModel());

        DataElementInternal snapshotElement = de.FromApiModel(null);
        snapshotElement.DeleteStatus = new DeleteStatus
        {
            IsHardDeleted = true,
            HardDeleted = DateTime.UtcNow,
        };
        InstanceInternal snapshotInstance = InstanceInternalTestFactory.Create(
            new Instance
            {
                Id = "1337/4914257c-9920-47a5-a37a-eae80f950767",
                InstanceOwner = new InstanceOwner { PartyId = "1337" },
                Data = [snapshotElement.ToApiModel()],
            },
            [snapshotElement],
            InternalId: 1,
            versions: new StorageVersions(2, 1)
        );
        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(new InstanceMutationApplyResult(false, [], snapshotInstance));

        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe30?delay=true";
        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(
            dataRepositoryMock,
            bearerAuthToken: token,
            mutationRepositoryMock: mutationRepositoryMock
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        mutationRepositoryMock.Verify(
            repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        Assert.Empty(capturedMutation.CreateDataElements);
        Assert.Empty(capturedMutation.DeleteDataElements);
        InstanceMutationDataElementUpdate capturedUpdate = Assert.Single(
            capturedMutation.UpdateDataElements
        );
        Assert.Equal(de.Id, capturedUpdate.DataElementId.ToString());
        Assert.True(capturedUpdate.IgnoreLock);
        Assert.Null(capturedUpdate.ExpectedCurrentBlobVersion);
        KeyValuePair<string, object> capturedProperty = Assert.Single(capturedUpdate.Properties);
        Assert.Equal("/deleteStatus", capturedProperty.Key);
        DeleteStatus capturedDeleteStatus = Assert.IsType<DeleteStatus>(capturedProperty.Value);
        Assert.True(capturedDeleteStatus.IsHardDeleted);
        Assert.NotNull(capturedDeleteStatus.HardDeleted);
        InstanceEvent deletedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(InstanceEventType.Deleted.ToString(), deletedEvent.EventType);
        Assert.Equal(de.Id, deletedEvent.DataId);
        dataRepositoryMock.Verify(
            dr =>
                dr.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Delete_Immediate_AggregateMutationApplied()
    {
        // Arrange
        DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe30");
        Mock<IDataRepository> dataRepositoryMock = new();
        Mock<IBlobRepository> blobRepositoryMock = new();
        Mock<IInstanceMutationRepository> mutationRepositoryMock = new();
        InstanceMutationCommit capturedMutation = null;
        dataRepositoryMock
            .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(de.FromApiModel());

        blobRepositoryMock
            .Setup(dr => dr.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(true);

        dataRepositoryMock
            .Setup(dr =>
                dr.ReadDetachedBlobVersions(It.IsAny<Guid>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync(Array.Empty<BlobVersionReferencesInternal>());

        mutationRepositoryMock
            .Setup(repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<Guid, long, InstanceMutationCommit, CancellationToken>(
                (_, _, mutation, _) => capturedMutation = mutation
            )
            .ReturnsAsync(
                new InstanceMutationApplyResult(
                    false,
                    [],
                    new InstanceInternal { Versions = new StorageVersions(8, 6) }
                )
            );

        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe30";
        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(
            dataRepositoryMock,
            bearerAuthToken: token,
            mutationRepositoryMock: mutationRepositoryMock
        );

        // Act
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "8",
            Assert.Single(response.Headers.GetValues(StorageHeaders.InstanceVersion))
        );
        Assert.Equal(
            "6",
            Assert.Single(response.Headers.GetValues(StorageHeaders.ProcessStateVersion))
        );
        dataRepositoryMock.VerifyAll();
        mutationRepositoryMock.Verify(
            repository =>
                repository.Apply(
                    It.IsAny<Guid>(),
                    It.IsAny<long>(),
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
        Assert.Contains(
            capturedMutation.DeleteDataElements,
            delete => delete.DataElement.Id == de.Id && delete.IgnoreLock
        );
        InstanceEvent deletedEvent = Assert.Single(capturedMutation.InstanceEvents);
        Assert.Equal(InstanceEventType.Deleted.ToString(), deletedEvent.EventType);
        Assert.Equal(de.Id, deletedEvent.DataId);
        dataRepositoryMock.Verify(
            dr =>
                dr.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Fact]
    public async Task Delete_EndUserDeletingAlreadyDeletedElement_NotFound()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";
        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OrgDeletingAlreadyDeletedElement_RepositoryUpdateNotCalled()
    {
        // Arrange
        DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe88");
        Mock<IDataRepository> dataRepositoryMock = new();
        dataRepositoryMock
            .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(de.FromApiModel());

        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88?delay=true";
        string token = PrincipalUtil.GetOrgToken("ttd");
        HttpClient client = GetTestClient(dataRepositoryMock, bearerAuthToken: token);

        // Act
        HttpResponseMessage response = await client.DeleteAsync(dataPathWithData);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        dataRepositoryMock.Verify(
            dr =>
                dr.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.IsAny<Dictionary<string, object>>(),
                    It.IsAny<DataElementUpdateContext>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    /// <summary>
    /// Scenario:
    ///   Update data element FileScanResult on newly created instance and data element.
    /// Expected:
    ///   Requests including platform access token should be granted access to endpoint.
    /// Success:
    ///   Response code is successful.
    /// </summary>
    [Fact]
    public async Task PutFileScanStatus_PlatformAccessIncluded_Ok()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
        HttpContent content = new StringContent("This is a blob file");

        HttpClient client = GetTestClient();
        HttpRequestMessage postRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{dataPathWithData}?dataType=default"
        );
        postRequest.Content = content;
        postRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            PrincipalUtil.GetToken(1337, 1337, 3)
        );
        HttpResponseMessage createDataElementResponse = await client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Created, createDataElementResponse.StatusCode);

        string dataElementContent = await createDataElementResponse.Content.ReadAsStringAsync();
        DataElement actual = JsonSerializer.Deserialize<DataElement>(
            dataElementContent,
            _serializerOptions
        );
        var dataElementId = actual.Id;

        var newFileScanStatus = new FileScanStatus { FileScanResult = FileScanResult.Clean };
        HttpRequestMessage putRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"{dataPathWithData}elements/{dataElementId}/filescanstatus"
        )
        {
            Content = JsonContent.Create(newFileScanStatus),
        };

        putRequest.Headers.Add("PlatformAccessToken", PrincipalUtil.GetAccessToken());

        // Act
        HttpResponseMessage setFileScanStatusResponse = await client.SendAsync(putRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, setFileScanStatusResponse.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   Update data element FileScanResult on newly created instance and data element.
    /// Expected:
    ///   End user should not be able to use this endpoint
    /// Success:
    ///   Response code is Forbidden.
    /// </summary>
    [Fact]
    public async Task PutFileScanStatusAsEndUser_MissingPlatformAccess_Forbidden()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
        HttpContent content = new StringContent("This is a blob file");

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);
        HttpResponseMessage createDataElementResponse = await client.PostAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        Assert.Equal(HttpStatusCode.Created, createDataElementResponse.StatusCode);

        string dataElementContent = await createDataElementResponse.Content.ReadAsStringAsync();
        DataElement actual = JsonSerializer.Deserialize<DataElement>(
            dataElementContent,
            _serializerOptions
        );
        var dataElementId = actual.Id;

        // Act
        var newFileScanStatus = new FileScanStatus { FileScanResult = FileScanResult.Clean };
        HttpResponseMessage setFileScanStatusResponse = await client.PutAsync(
            $"{dataPathWithData}elements/{dataElementId}/filescanstatus",
            JsonContent.Create(newFileScanStatus)
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, setFileScanStatusResponse.StatusCode);
    }

    [Fact]
    public async Task Get_DataElementExists_PlatformAccessIncluded_Ok()
    {
        // Arrange
        const string dataElementId = "887c5e56-6f73-494a-9730-6ebd11bffe30";
        const string partyId = "1337";
        const string instanceId = "bc19107c-508f-48d9-bcd7-54ffec905306";
        const string dataPath = $"{_versionPrefix}/instances/{partyId}/{instanceId}";

        Mock<IDataRepository> dataRepositoryMock = new();
        dataRepositoryMock
            .Setup(dr => dr.Exists(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        HttpClient client = GetTestClient(dataRepositoryMock: dataRepositoryMock);

        HttpRequestMessage getRequest = new(
            HttpMethod.Get,
            $"{dataPath}/dataelementexists/{dataElementId}"
        );

        getRequest.Headers.Add("PlatformAccessToken", PrincipalUtil.GetAccessToken());

        // Act
        HttpResponseMessage response = await client.SendAsync(getRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.True(bool.Parse(content));
    }

    [Fact]
    public async Task Get_DataElementExists_AsEndUser_MissingPlatformAccess_Forbidden()
    {
        // Arrange
        const string dataElementId = "887c5e56-6f73-494a-9730-6ebd11bffe30";
        const string partyId = "1337";
        const string instanceId = "bc19107c-508f-48d9-bcd7-54ffec905306";
        const string dataPath = $"{_versionPrefix}/instances/{partyId}/{instanceId}";

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(bearerAuthToken: token);

        // Act
        HttpResponseMessage setFileScanStatusResponse = await client.GetAsync(
            $"{dataPath}/dataelementexists/{dataElementId}"
        );

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, setFileScanStatusResponse.StatusCode);
    }

    /// <summary>
    /// Scenario:
    ///   Post data but stream is empty and empty blob attempted persisted.
    /// Expected:
    ///   Blob should be deleted from blob storage.
    /// Success:
    ///   Response code is BadRequest.
    /// </summary>
    [Fact]
    public async Task CreateAndUploadBlob_StreamIsEmpty_BadRequest()
    {
        // Arrange
        string dataPathWithData =
            $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
        HttpContent content = new StringContent("This is a blob file");

        Mock<IBlobRepository> repoMock = new();
        repoMock
            .Setup(r =>
                r.WriteBlob(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int?>()
                )
            )
            .ReturnsAsync((0, DateTime.UtcNow));

        repoMock
            .Setup(r => r.DeleteBlob(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(true);

        string token = PrincipalUtil.GetToken(1337, 1337, 3);
        HttpClient client = GetTestClient(null, repoMock, null, token);

        // Act
        HttpResponseMessage response = await client.PostAsync(
            $"{dataPathWithData}?dataType=default",
            content
        );

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        repoMock.VerifyAll();
    }

    private static string ExpectedDuplicateDataElementMutationIdsResponse(Guid dataElementId) =>
        $"\"dataElementId '{dataElementId}' is referenced by more than one operation.\"";

    private static InstanceInternal CreateMutationInstance(
        ProcessState process,
        InstanceStatus status
    )
    {
        Instance instance = new()
        {
            Id = $"{SensitiveDataApp.InstanceOwnerPartyId}/{SensitiveDataApp.InstanceGuid}",
            InstanceOwner = new InstanceOwner { PartyId = SensitiveDataApp.InstanceOwnerPartyId },
            AppId = "ttd/sensitive-data",
            Org = "ttd",
            Process = process,
            Status = status,
            Data = [],
        };

        return InstanceInternalTestFactory.Create(instance, [], InternalId: 1);
    }

    private static Mock<IInstanceRepository> CreateMutationInstanceRepository(
        InstanceInternal storedInstance
    )
    {
        Mock<IInstanceRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.GetOne(
                    Guid.Parse(SensitiveDataApp.InstanceGuid),
                    true,
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(storedInstance);
        return repositoryMock;
    }

    private static Mock<IInstanceMutationRepository> CreatePersistingMutationRepository(
        InstanceInternal storedInstance,
        Action<InstanceMutationCommit> captureMutation
    )
    {
        Mock<IInstanceMutationRepository> repositoryMock = new();
        repositoryMock
            .Setup(repository =>
                repository.Apply(
                    Guid.Parse(SensitiveDataApp.InstanceGuid),
                    storedInstance.InternalId,
                    It.IsAny<InstanceMutationCommit>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(
                (Guid _, long _, InstanceMutationCommit mutation, CancellationToken _) =>
                {
                    captureMutation(mutation);
                    if (mutation.InstanceUpdateProperties.Contains(nameof(InstanceInternal.Status)))
                    {
                        storedInstance.Status = mutation.InstanceUpdates.Status;
                    }

                    if (
                        mutation.InstanceUpdateProperties.Contains(nameof(InstanceInternal.Process))
                    )
                    {
                        storedInstance.Process = mutation.InstanceUpdates.Process;
                    }

                    return new InstanceMutationApplyResult(false, [], storedInstance);
                }
            );
        return repositoryMock;
    }

    private HttpClient GetTestClient(
        Mock<IDataRepository> dataRepositoryMock = null,
        Mock<IBlobRepository> blobRepositoryMock = null,
        Mock<IFileScanQueueClient> fileScanMock = null,
        string bearerAuthToken = null,
        Mock<IInstanceMutationRepository> mutationRepositoryMock = null,
        Mock<IInstanceRepository> instanceRepositoryMock = null
    )
    {
        if (mutationRepositoryMock is null)
        {
            mutationRepositoryMock = new Mock<IInstanceMutationRepository>();
            mutationRepositoryMock
                .Setup(repository =>
                    repository.Apply(
                        It.IsAny<Guid>(),
                        It.IsAny<long>(),
                        It.IsAny<InstanceMutationCommit>(),
                        It.IsAny<CancellationToken>()
                    )
                )
                .ReturnsAsync(
                    new InstanceMutationApplyResult(
                        false,
                        [],
                        new InstanceInternal { Versions = new StorageVersions(2, 1) }
                    )
                );
        }

        // No setup required for these services. They are not in use by the InstanceController
        Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
        Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();
        Mock<IMessageBus> busMock = new Mock<IMessageBus>();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(ServiceUtil.GetAppsettingsPath())
                .Build();
            builder.ConfigureAppConfiguration(
                (hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                }
            );

            builder.ConfigureTestServices(services =>
            {
                services.AddMockRepositories();

                if (blobRepositoryMock is not null)
                {
                    services.AddSingleton(blobRepositoryMock.Object);
                }

                if (dataRepositoryMock is not null)
                {
                    services.AddSingleton(dataRepositoryMock.Object);
                }

                if (fileScanMock is not null)
                {
                    services.AddSingleton(fileScanMock.Object);
                }

                if (mutationRepositoryMock is not null)
                {
                    services.AddSingleton(mutationRepositoryMock.Object);
                }

                if (instanceRepositoryMock is not null)
                {
                    services.AddSingleton(instanceRepositoryMock.Object);
                }

                services.AddSingleton<
                    IPostConfigureOptions<JwtCookieOptions>,
                    JwtCookiePostConfigureOptionsStub
                >();
                services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();

                services.AddSingleton(keyVaultWrapper.Object);
                services.AddSingleton(partiesWrapper.Object);
                services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                services.AddSingleton(busMock.Object);
            });
        });

        var client = factory.CreateClient();
        if (!string.IsNullOrEmpty(bearerAuthToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                bearerAuthToken
            );
        }

        _testTelemetry = factory.Services.GetRequiredService<TestTelemetry>();

        return client;
    }

    public enum AuthenticationType
    {
        User,
        Org,
    }

    private static class VersionedBlobElement
    {
        public const string InstanceGuid = "649388f0-a2c0-4774-bd11-c870223ed819";
        public const string DataElementId = "7d1c4b8e-3f2a-4c6d-9e5b-1a2b3c4d5e6f";
        public const string BlobStoragePath =
            "tdd/endring-av-navn/" + InstanceGuid + "/data-elements/AZfQZ9nHc0eLm4Xv2R1qAA";
    }

    private static class SensitiveDataApp
    {
        public const string InstanceGuid = "99194777-a691-433a-ace1-225e9a691653";
        public const string InstanceOwnerPartyId = "1337";

        public static class DataTypes
        {
            public const string Default = "model";
            public const string SensitiveRead = "sensitive-data-read";
            public const string SensitiveWrite = "sensitive-data-write";
            public const string SensitiveBoth = "sensitive-data-both";
        }

        public static class DataElements
        {
            public const string Default = "70d122f8-0cae-44f4-8cd5-2887c251a959";
            public const string SensitiveRead = "15c0fa5d-a243-4fa2-882b-002bb60b6227";
            public const string SensitiveWrite = "6448a556-2db0-4279-b535-13e7f9c05809";
            public const string SensitiveBoth = "bb64df50-fdb1-456b-943e-9c32f524943e";
        }

        public static string GetInstanceUrl() =>
            $"{_versionPrefix}/instances/{InstanceOwnerPartyId}/{InstanceGuid}";
    }
}
