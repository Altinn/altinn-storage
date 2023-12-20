using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Controllers;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingControllers
{
    /// <summary>
    /// Represents a collection of integration tests of the <see cref="DataLockController"/>.
    /// Testdata setup:
    /// Users and allowed actions on party 500004 for app tdd/read-write-unlock Task_1:
    /// 1337 (DAGL): write
    /// 1 (REGNA): unlock
    /// 3 (A0212): read
    /// 5 (A0236): reject
    /// 10016 (SIGNE): read, write, unlock
    /// Instances:
    /// 500004/4c67392f-36c6-42dc-998f-c367e771dccc CurrentTask is Task_1 data unlocked
    /// 500004/4c67392f-36c6-42dc-998f-c367e771dcdd CurrentTask is Task_1 data locked
    /// 500004/4c67392f-36c6-42dc-998f-c367e771dcde CurrentTask is Task_2 (No users have any rights in this Task)
    /// DataElements and their locked status:
    /// 998c5e56-6f73-494a-9730-6ebd11bffe88: false (unlocked)
    /// 998c5e56-6f73-494a-9730-6ebd11bfff99: true (locked)
    /// </summary>
    public class DataLockControllerTests : IClassFixture<TestApplicationFactory<DataLockController>>
    {
        private readonly TestApplicationFactory<DataLockController> _factory;
        private readonly string _versionPrefix = "/storage/api/v1";

        /// <summary>
        /// Initializes a new instance of the <see cref="DataLockControllerTests"/> class.
        /// </summary>
        /// <param name="factory">Platform storage fixture.</param>
        public DataLockControllerTests(TestApplicationFactory<DataLockController> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async void User_with_write_is_allowed_to_lock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dccc/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var stream = await response.Content.ReadFromJsonAsync<DataElement>();
            AssertDataLockHasCorrectStatus(stream, true);
        }
        
        [Fact]
        public async void User_with_write_is_allowed_to_lock_already_locked_dataelement()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var stream = await response.Content.ReadFromJsonAsync<DataElement>();
            AssertDataLockHasCorrectStatus(stream, true);
        }
        
        [Fact]
        public async void PUT_lock_return_NotFound_when_datalement_not_present()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff00/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        
        [Fact]
        public async void User_with_read_write_unlock_is_allowed_to_lock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dccc/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(10016, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var stream = await response.Content.ReadFromJsonAsync<DataElement>();
            AssertDataLockHasCorrectStatus(stream, true);
        }
        
        [Fact]
        public async void User_with_read_is_not_allowed_to_lock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dccc/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        [Fact]
        public async void User_with_unlock_is_not_allowed_to_lock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dccc/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        [Fact]
        public async void User_with_write_on_Task_1_is_not_allowed_to_lock_data_if_current_task_is_Task_2()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcde/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        [Fact]
        public async void User_with_write_is_allowed_to_unlock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var stream = await response.Content.ReadFromJsonAsync<DataElement>();
            AssertDataLockHasCorrectStatus(stream, false);
        }
        
        [Fact]
        public async void User_with_write_is_allowed_to_unlock_already_unlocked_dataelement()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var stream = await response.Content.ReadFromJsonAsync<DataElement>();
            AssertDataLockHasCorrectStatus(stream, false);
        }
        
        [Fact]
        public async void User_with_read_write_unlock_is_allowed_to_unlock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bffe88/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(10016, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var stream = await response.Content.ReadFromJsonAsync<DataElement>();
            AssertDataLockHasCorrectStatus(stream, false);
        }
        
        [Fact]
        public async void Users_not_allowed_to_lock_when_partyId_not_same_as_instance_partyId()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcde/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.PutAsync($"{dataPathWithData}", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        [Fact]
        public async void DELETE_lock_return_NotFound_when_datalement_not_present()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff00/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        
        [Fact]
        public async void User_with_read_is_not_allowed_to_unlock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(3, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        [Fact]
        public async void User_with_unlock_is_allowed_to_unlock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertDataLockHasCorrectStatus(await response.Content.ReadFromJsonAsync<DataElement>(), false);
        }
        
        [Fact]
        public async void User_with_reject_is_allowed_to_unlock()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcdd/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(5, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertDataLockHasCorrectStatus(await response.Content.ReadFromJsonAsync<DataElement>(), false);
        }
        
        [Fact]
        public async void Users_not_allowed_to_unlock_when_user_has_no_allowed_actions_on_current_task()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcde/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1, 500004, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        [Fact]
        public async void Users_not_allowed_to_unlock_when_partyId_not_same_as_instance_partyId()
        {
            string dataPathWithData = $"{_versionPrefix}/instances/500004/4c67392f-36c6-42dc-998f-c367e771dcde/data/998c5e56-6f73-494a-9730-6ebd11bfff99/lock";

            HttpClient client = GetTestClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", PrincipalUtil.GetToken(1337, 1337, 3));
            HttpResponseMessage response = await client.DeleteAsync($"{dataPathWithData}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        
        private static void AssertDataLockHasCorrectStatus(DataElement dataElement, bool expectedLockStatus)
        {
            if (dataElement == null)
            {
                Assert.Fail("Data element is null");
            }
            
            if (expectedLockStatus != dataElement.Locked)
            {
                Assert.Fail("Data element lock status is not as expected. Expected: " + expectedLockStatus + " Actual: " + dataElement.Locked);
            }
        }

        private HttpClient GetTestClient(Mock<IDataRepository> repositoryMock = null)
        {
            // No setup required for these services. They are not in use by the InstanceController
            Mock<ISasTokenProvider> sasTokenProvider = new Mock<ISasTokenProvider>();
            Mock<IKeyVaultClientWrapper> keyVaultWrapper = new Mock<IKeyVaultClientWrapper>();
            Mock<IPartiesWithInstancesClient> partiesWrapper = new Mock<IPartiesWithInstancesClient>();

            HttpClient client = _factory.WithWebHostBuilder(builder =>
            {
                IConfiguration configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
                builder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddConfiguration(configuration);
                });

                builder.ConfigureTestServices(services =>
                {
                    services.AddMockRepositories();

                    if (repositoryMock is not null)
                    {
                        services.AddSingleton(repositoryMock.Object);
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
