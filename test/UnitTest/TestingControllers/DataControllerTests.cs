using System;
using System.Collections.Generic;
using System.IO;
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
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.Tests.Mocks;
using Altinn.Platform.Storage.UnitTest.Fixture;
using Altinn.Platform.Storage.UnitTest.Mocks;
using Altinn.Platform.Storage.UnitTest.Mocks.Authentication;
using Altinn.Platform.Storage.UnitTest.Mocks.Repository;
using Altinn.Platform.Storage.UnitTest.Utils;
using Altinn.Platform.Storage.Wrappers;

using AltinnCore.Authentication.JwtCookie;

using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    /// <summary>
    /// Represents a collection of integration tests of the <see cref="DataController"/>.
    /// </summary>
    public class DataControllerTests : IClassFixture<TestApplicationFactory<DataController>>
    {
        private readonly TestApplicationFactory<DataController> _factory;
        private readonly string _versionPrefix = "/storage/api/v1";
        private readonly JsonSerializerOptions _serializerOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="DataControllerTests"/> class.
        /// </summary>
        /// <param name="factory">Platform storage fixture.</param>
        public DataControllerTests(TestApplicationFactory<DataController> factory)
        {
            _factory = factory;
            _serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
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
        public async void Post_NewData_Ok()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async void Post_NewDataThatRequiresFileScan_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();

            HttpClient client = GetTestClient(null, null, fileScanMock);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default_with_fileScan", content);

            // Assert
            fileScanMock.Verify(f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(responseContent, _serializerOptions);

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
        public async void Post_NewData_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/69c259d1-9c1f-4ab6-9d8b-5c210042dc4f/data";
            HttpContent content = new StringContent("This is a blob file");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1337, 3));
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

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
        public async void Post_NewData_ToLowAuthenticationLevel()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/69c259d1-9c1f-4ab6-9d8b-5c210042dc4f/data";
            HttpContent content = new StringContent("This is a blob file");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 1337, 0));
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        public async void OverwriteData_UpdateData_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";
            HttpContent content = new StringContent("This is a blob file with updated data");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", content);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(responseContent, _serializerOptions);

            Assert.Equal(FileScanResult.NotApplicable, actual.FileScanResult);
        }

        [Fact]
        public async void OverwriteData_UpdateDataOnDataTypeWithFileScan_StartsFileScan()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/50c60b30-cb9a-435b-a31e-bbce47c2b936";
            HttpContent content = new StringContent("This is a blob file with updated data");

            Mock<IFileScanQueueClient> fileScanMock = new Mock<IFileScanQueueClient>();

            HttpClient client = GetTestClient(null, null, fileScanMock);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", content);

            // Assert
            fileScanMock.Verify(f => f.EnqueueFileScan(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            string responseContent = await response.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(responseContent, _serializerOptions);

            Assert.Equal(FileScanResult.Pending, actual.FileScanResult);
        }

        [Fact]
        public async void OverwriteData_DataElementDoesNotExist_ReturnsNotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11111111-6681-47a1-9626-fcf6c27308a5";
            HttpContent content = new StringContent("This is a blob file with updated data");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        public async void OverwriteData_UpdateData_Conflict()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/6aa47207-f089-4c11-9cb2-f00af6f66a47/data/24bfec2e-c4ce-4e82-8fa9-aa39da329fd5";
            HttpContent content = new StringContent("This is a blob file with updated data");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async void Delete_DataElement_Ok()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async void Delete_DataElementDoesNotExist_ReturnsNotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11111111-6681-47a1-9626-fcf6c27308a5";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async void Delete_DataElement_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/649388f0-a2c0-4774-bd11-c870223ed819/data/11f7c994-6681-47a1-9626-fcf6c27308a5";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElement_Ok()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElementDoesNotExists_ReturnsNotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/11111111-8eed-4d1d-9d75-9239c40724e9";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElements_Ok()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElementsAsEndUser_HardDeletedFilteredOut()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/dataelements/";
            int expectedCount = 2;

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");
            string content = await response.Content.ReadAsStringAsync();
            DataElementList actual = JsonSerializer.Deserialize<DataElementList>(content, _serializerOptions);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedCount, actual.DataElements.Count);
        }

        [Fact]
        public async void Get_DataElementsAsAppOwner_HardDeletedIncluded()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/dataelements/";
            int expectedCount = 3;

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetOrgToken("ttd"));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");
            string content = await response.Content.ReadAsStringAsync();
            DataElementList actual = JsonSerializer.Deserialize<DataElementList>(content, _serializerOptions);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedCount, actual.DataElements.Count);
        }

        [Fact]
        public async void Get_DataElements_To_Low_Auth_Level()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 1));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElements_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/dataelements/";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElement_NotAuthorized()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 1, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElement_ToLowAuthenticationLevel()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 1));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElement_Org_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/ca9da17c-904a-44d2-9771-a5420acfbcf3/data/28023597-516b-4a71-a77c-d3736912abd5";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetOrgToken("tdd"));

            // Act
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElementAsEndUser_HardDeleted_NotFound()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async void Get_DataElementAsAppOwner_HardDeletedIncluded()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetOrgToken("ttd"));
            HttpResponseMessage response = await client.GetAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async void Delete_Delayed_AutoDeleteMissing_BadRequest()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/d91fd644-1028-4efd-924f-4ca187354514/data/f4feb26c-8eed-4d1d-9d75-9239c40724e9?delay=true";
            string expected = "\"DataType default does not support delayed deletion\"";
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");
            string actual = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public async void Delete_Delayed_UpdateMethodCalledInRepository()
        {
            // Arrange
            DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe30");
            Mock<IDataRepository> dataRepositoryMock = new();
            dataRepositoryMock
                .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync(de);

            dataRepositoryMock
                .Setup(dr => dr.Update(
                    It.IsAny<Guid>(),
                    It.IsAny<Guid>(),
                    It.Is<Dictionary<string, object>>(propertyList => VerifyDeleteStatusPresentInDictionary(propertyList))))
                .ReturnsAsync(new DataElement());

            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe30?delay=true";
            HttpClient client = GetTestClient(dataRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            dataRepositoryMock.VerifyAll();
        }

        private static bool VerifyDeleteStatusPresentInDictionary(Dictionary<string, object> propertyList)
        {
            if (!propertyList.ContainsKey("/deleteStatus"))
            {
                return false;
            }

            if (propertyList.Count > 1)
            {
                // property list should only contain one element when called from this controller method
                return false;
            }

            if (!propertyList.TryGetValue("/deleteStatus", out object value))
            {
                return false;
            }

            DeleteStatus actual = (DeleteStatus)value;
            if (!actual.IsHardDeleted || actual.HardDeleted == null)
            {
                return false;
            }

            return true;
        }

        [Fact]
        public async void Delete_Immediate_DeleteMethodCalledInRepository()
        {
            // Arrange
            DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe30");
            Mock<IDataRepository> dataRepositoryMock = new();
            Mock<IBlobRepository> blobRepositoryMock = new();
            dataRepositoryMock
                           .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync(de);

            blobRepositoryMock
                .Setup(dr => dr.DeleteBlob(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            dataRepositoryMock
                .Setup(dr => dr.Delete(It.IsAny<DataElement>()))
                .ReturnsAsync(true);

            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe30";
            HttpClient client = GetTestClient(dataRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            dataRepositoryMock.VerifyAll();
        }

        [Fact]
        public async void Delete_EndUserDeletingAlreadyDeletedElement_NotFound()
        {
            // Arrange      
            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88";
            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async void Delete_OrgDeletingAlreadyDeletedElement_RepositoryUpdateNotCalled()
        {
            // Arrange
            DataElement de = TestDataUtil.GetDataElement("887c5e56-6f73-494a-9730-6ebd11bffe88");
            Mock<IDataRepository> dataRepositoryMock = new();
            dataRepositoryMock = new();
            dataRepositoryMock
                .Setup(dr => dr.Read(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync(de);

            string dataPathWithData = $"{_versionPrefix}/instances/1337/4914257c-9920-47a5-a37a-eae80f950767/data/887c5e56-6f73-494a-9730-6ebd11bffe88?delay=true";
            HttpClient client = GetTestClient(dataRepositoryMock);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetOrgToken("ttd"));

            // Act
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            dataRepositoryMock.Verify(dr => dr.Update(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>()), Times.Never);
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
        public async void PutFileScanStatus_PlatformAccessIncluded_Ok()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            HttpClient client = GetTestClient();
            HttpRequestMessage postRequest = new HttpRequestMessage(HttpMethod.Post, $"{dataPathWithData}?dataType=default");
            postRequest.Content = content;
            postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage createDataElementResponse = await client.SendAsync(postRequest);

            Assert.Equal(HttpStatusCode.Created, createDataElementResponse.StatusCode);

            string dataElementContent = await createDataElementResponse.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(dataElementContent, _serializerOptions);
            var dataElementId = actual.Id;

            var newFileScanStatus = new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean
            };
            HttpRequestMessage putRequest = new HttpRequestMessage(HttpMethod.Put, $"{dataPathWithData}elements/{dataElementId}/filescanstatus")
            {
                Content = JsonContent.Create(newFileScanStatus)
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
        public async void PutFileScanStatusAsEndUser_MissingPlatformAccess_Forbidden()
        {
            // Arrange
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage createDataElementResponse = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            Assert.Equal(HttpStatusCode.Created, createDataElementResponse.StatusCode);

            string dataElementContent = await createDataElementResponse.Content.ReadAsStringAsync();
            DataElement actual = JsonSerializer.Deserialize<DataElement>(dataElementContent, _serializerOptions);
            var dataElementId = actual.Id;

            // Act
            var newFileScanStatus = new FileScanStatus
            {
                FileScanResult = FileScanResult.Clean
            };
            HttpResponseMessage setFileScanStatusResponse = await client.PutAsync($"{dataPathWithData}elements/{dataElementId}/filescanstatus", JsonContent.Create(newFileScanStatus));

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
            string dataPathWithData = $"{_versionPrefix}/instances/1337/bc19107c-508f-48d9-bcd7-54ffec905306/data";
            HttpContent content = new StringContent("This is a blob file");

            Mock<IBlobRepository> repoMock = new();
            repoMock.
                Setup(r => r.WriteBlob(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync((0, DateTime.UtcNow));

            repoMock
                .Setup(r => r.DeleteBlob(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            HttpClient client = GetTestClient(null, repoMock, null);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));

            // Act
            HttpResponseMessage response = await client.PostAsync($"{dataPathWithData}?dataType=default", content);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            repoMock.VerifyAll();
        }

        private HttpClient GetTestClient(Mock<IDataRepository> dataRepositoryMock = null, Mock<IBlobRepository> blobRepositoryMock = null, Mock<IFileScanQueueClient> fileScanMock = null)
        {
            // No setup required for these services. They are not in use by the InstanceController
            Mock<ISasTokenProvider> sasTokenProvider = new Mock<ISasTokenProvider>();
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();

            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
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

                    services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, JwtCookiePostConfigureOptionsStub>();
                    services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProviderMock>();

                    services.AddSingleton(sasTokenProvider.Object);
                    services.AddSingleton(keyVaultWrapper.Object);
                    services.AddSingleton(partiesWrapper.Object);
                    services.AddSingleton<IPDP, PepWithPDPAuthorizationMockSI>();
                });
            }).CreateClient();

            return client;
        }
    }
}
