using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Altinn.Authorization.ABAC.Xacml.JsonProfile;
using Altinn.Common.PEP.Interfaces;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Altinn.Platform.Storage.UnitTest.Mocks;
using AltinnCore.Authentication.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingServices
{
    public class AuthorizationServiceTest
    {
        private const string Org = "tdd";
        private const string App = "test-applikasjon-1";
        private const string UrnName = "urn:name";
        private const string UrnAuthLv = "urn:altinn:authlevel";
        private const string UrnUserId = "urn:altinn:userid";

        private readonly AuthorizationService _authzService;
        private readonly IPDP _pdpMockSI;
        private readonly Mock<IPDP> _pdpSimpleMock;
        private readonly Mock<IInstanceRepository> _instanceRepository = new();
        private readonly Mock<IClaimsPrincipalProvider> _claimsPrincipalProviderMock = new();

        public AuthorizationServiceTest()
        {
            _pdpSimpleMock = new Mock<IPDP>();
            _pdpMockSI = new PepWithPDPAuthorizationMockSI(_instanceRepository.Object);
            var generalSettings = new GeneralSettings { AuthorizeA2ListInstancesDelete = true };
            var options = Options.Create(generalSettings);
            _authzService = new AuthorizationService(
                _pdpMockSI, _claimsPrincipalProviderMock.Object, Mock.Of<ILogger<AuthorizationService>>(), options);
        }

        [Fact]
        public async Task GetDecisionForRequest_ConfirmPDPCalled()
        {
            var res = new XacmlJsonResponse
            {
                Response = new List<XacmlJsonResult>()
                {
                    new XacmlJsonResult
                    {
                        Decision = "Permit"
                    }
                }
            };

            _pdpSimpleMock.Setup(pdp => pdp.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()))
                .ReturnsAsync(res);

            var generalSettings = new GeneralSettings { AuthorizeA2ListInstancesDelete = true };
            var options = Options.Create(generalSettings);

            var sut = new AuthorizationService(
                _pdpSimpleMock.Object, _claimsPrincipalProviderMock.Object, Mock.Of<ILogger<AuthorizationService>>(), options);
            await sut.GetDecisionForRequest(new XacmlJsonRequestRoot());

            _pdpSimpleMock.Verify(m => m.GetDecisionForRequest(It.IsAny<XacmlJsonRequestRoot>()), Times.Once());
        }

        [Fact]
        public void UserHasRequiredScope_CaseIgnored_ReturnsTrue()
        {
            // Arrange
            string reqiured = "altinn:serviceowner/instances.read";

            var claims = new List<Claim>();
            claims.Add(new Claim("urn:altinn:scope", "ALTINN:SERVICEOWNER/INSTANCES.READ", ClaimValueTypes.String, "maskinporten"));

            var identity = new ClaimsIdentity("AuthenticationTypes.Federation");
            identity.AddClaims(claims);
            var principal = new ClaimsPrincipal(identity);
            _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(principal);
            
            // Act
            var actual = _authzService.UserHasRequiredScope(new List<string> { reqiured });

            // Assert
            Assert.True(actual);
        }

        [Fact]
        public void UserHasRequiredScope_MissingRequiredScope_ReturnsFalse()
        {
            // Arrange
            string reqiured = "altinn:serviceowner/instances.read";

            var claims = new List<Claim>();
            string issuer = "www.altinn.no";
            claims.Add(new Claim("urn:altinn:org", "nav", ClaimValueTypes.String, issuer));
            claims.Add(new Claim("urn:altinn:orgNumber", "123456789", ClaimValueTypes.Integer32, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticateMethod, "Mock", ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticationLevel, "3", ClaimValueTypes.Integer32, issuer));
            claims.Add(new Claim("urn:altinn:scope", "altinn:random.scope", ClaimValueTypes.String, "maskinporten"));

            var identity = new ClaimsIdentity("AuthenticationTypes.Federation");
            identity.AddClaims(claims);
            var principal = new ClaimsPrincipal(identity);

            _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(principal);

            // Act
            var actual = _authzService.UserHasRequiredScope(new List<string> { reqiured });

            // Assert
            Assert.False(actual);
        }

        /// <summary>
        /// Test case: Send attributes and creates multiple request out of it 
        /// Expected: All values sent in will be created to attributes
        /// </summary>
        [Fact]
        public void CreateXacmlJsonMultipleRequest_TC01()
        {
            // Arrange
            List<string> actionTypes = new List<string> { "read", "write" };
            List<Instance> instances = CreateInstances();

            // Act
            XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(CreateUserClaims(1), instances, actionTypes);

            // Assert
            // Checks it has the right number of attributes in each category 
            Assert.Single(requestRoot.Request.AccessSubject);
            Assert.Equal(2, requestRoot.Request.Action.Count);
            Assert.Equal(3, requestRoot.Request.Resource.Count);
            Assert.Equal(4, requestRoot.Request.Resource.First().Attribute.Count);
            Assert.Equal(6, requestRoot.Request.MultiRequests.RequestReference.Count);

            foreach (var referenceId in requestRoot.Request.MultiRequests.RequestReference)
            {
                Assert.Equal(3, referenceId.ReferenceId.Count);
            }
        }

        /// <summary>
        /// Test case: Send in user with claims that is null
        /// Expected: throws ArgumentNullException
        /// </summary>
        [Fact]
        public void CreateXacmlJsonMultipleRequest_TC02()
        {
            // Arrange
            List<string> actionTypes = new List<string> { "read", "write" };
            List<Instance> instances = CreateInstances();

            // Act & Assert 
            Assert.Throws<ArgumentNullException>(() => AuthorizationService.CreateMultiDecisionRequest(null, instances, actionTypes));
        }

        /// <summary>
        /// Test case: Migrated A2 instances
        /// Expected: Dummy end events are added
        /// </summary>
        [Fact]
        public void CreateXacmlJsonMultipleRequest_TC03()
        {
            // Arrange
            List<string> actionTypes = new List<string> { "read", "write" };
            List<Instance> instances = CreateInstances();
            foreach (Instance instance in instances)
            {
                // Add data values to the instances
                instance.DataValues = new() { { "A2ArchRef", "test" } };
            }

            // Act
            XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(CreateUserClaims(1), instances, actionTypes);

            // Assert
            requestRoot.Request.Resource.ForEach(resource =>
            {
                Assert.Contains(resource.Attribute, attr => attr.AttributeId == "urn:altinn:end-event" && attr.Value == "MigratedA1A2");
            });
        }

        /// <summary>
        /// Test case: Migrated A1 instances
        /// Expected: Dummy end events are added
        /// </summary>
        [Fact]
        public void CreateXacmlJsonMultipleRequest_TC04()
        {
            // Arrange
            List<string> actionTypes = new List<string> { "read", "write" };
            List<Instance> instances = CreateInstances();
            foreach (Instance instance in instances)
            {
                // Add data values to the instances
                instance.DataValues = new() { { "A1ArchRef", "test" } };
            }

            // Act
            XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(CreateUserClaims(1), instances, actionTypes);

            // Assert
            requestRoot.Request.Resource.ForEach(resource =>
            {
                Assert.Contains(resource.Attribute, attr => attr.AttributeId == "urn:altinn:end-event" && attr.Value == "MigratedA1A2");
            });
        }

        /// <summary>
        /// Test case: Normal A3 instances
        /// Expected: Dummy end events are not added
        /// </summary>
        [Fact]
        public void CreateXacmlJsonMultipleRequest_TC05()
        {
            // Arrange
            List<string> actionTypes = new List<string> { "read", "write" };
            List<Instance> instances = CreateInstances();
            foreach (Instance instance in instances)
            {
                // Add data values to the instances
                instance.DataValues = new() { { "SomeValue", "test" } };
            }

            // Act
            XacmlJsonRequestRoot requestRoot = AuthorizationService.CreateMultiDecisionRequest(CreateUserClaims(1), instances, actionTypes);

            // Assert
            requestRoot.Request.Resource.ForEach(resource =>
            {
                Assert.DoesNotContain(resource.Attribute, attr => attr.AttributeId == "urn:altinn:end-event" && attr.Value == "MigratedA1A2");
            });
        }

        /// <summary>
        /// Test case: Authorize an convert emtpy list of instances to messageboxInstances
        /// Expected: An empty list is returned.
        /// </summary>
        [Fact]
        public async Task AuthorizeMesseageBoxInstances_TC01_EmptyList()
        {
            // Arrange
            List<MessageBoxInstance> expected = new List<MessageBoxInstance>();
            List<Instance> instances = new List<Instance>();
            _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(CreateUserClaims(3));

            // Act
            List<MessageBoxInstance> actual = await _authzService.AuthorizeMesseageBoxInstances(instances, false);

            // Assert
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Test case: Authorize a list of instances to messageboxInstances
        /// Expected: Delete and write is not allowed, so the AllowDelete and AuthorizedForWrite properties are false.
        /// </summary>
        [Fact]
        public async Task AuthorizeMesseageBoxInstances_TC02_ListWithDeleteAndWriteTrue()
        {
            // Arrange
            List<Instance> instances = CreateInstances();
            _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(CreateUserClaims(3));
            var options = Options.Create(new GeneralSettings { AuthorizeA2ListInstancesDelete = true, AuthorizeA2ListInstancesWrite = true });
            var authzService = new AuthorizationService(
                _pdpMockSI, _claimsPrincipalProviderMock.Object, Mock.Of<ILogger<AuthorizationService>>(), options);

            // Act
            List<MessageBoxInstance> actual = await authzService.AuthorizeMesseageBoxInstances(instances, false);

            // Assert
            Assert.Equal(3, actual.Count);
            Assert.True(actual[0].AllowDelete);
            Assert.True(actual[0].AuthorizedForWrite);
        }

        /// <summary>
        /// Test case: Authorize a list of instances to messageboxInstances
        /// Expected: Delete and write is not allowed, so the AllowDelete and AuthorizedForWrite properties are false.
        /// </summary>
        [Fact]
        public async Task AuthorizeMesseageBoxInstances_TC03_ListWithDeleteAndWriteFalse()
        {
            // Arrange
            List<Instance> instances = CreateInstances();
            _claimsPrincipalProviderMock.Setup(c => c.GetUser()).Returns(CreateUserClaims(3));
            var options = Options.Create(new GeneralSettings { AuthorizeA2ListInstancesDelete = false, AuthorizeA2ListInstancesWrite = false });
            var authzService = new AuthorizationService(
                _pdpMockSI, _claimsPrincipalProviderMock.Object, Mock.Of<ILogger<AuthorizationService>>(), options);

            // Act
            List<MessageBoxInstance> actual = await authzService.AuthorizeMesseageBoxInstances(instances, false);

            // Assert
            Assert.Equal(3, actual.Count);
            Assert.False(actual[0].AllowDelete);
            Assert.False(actual[0].AuthorizedForWrite);
        }

        private static ClaimsPrincipal CreateUserClaims(int userId)
        {
            // Create the user
            List<Claim> claims = new()
            {
                // type, value, valuetype, issuer
                new Claim(UrnName, "Ola", "string", "org"),
                new Claim(UrnAuthLv, "2", "string", "org"),
                new Claim(UrnUserId, $"{userId}", "string", "org")
            };

            ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity(claims));

            return user;
        }

        private static List<Instance> CreateInstances()
        {
            List<Instance> instances = new List<Instance>
            {
                new Instance
                {
                    Id = "1000/" + Guid.NewGuid(),
                    Process = new ProcessState
                    {
                        CurrentTask = new ProcessElementInfo
                        {
                            Name = "test_task"
                        }
                    },
                    InstanceOwner = new InstanceOwner
                    {
                        PartyId = "1000"
                    },
                    AppId = Org + "/" + App,
                    Org = Org,
                    Created = DateTime.UtcNow
                },
                new Instance
                {
                    Id = "1002/" + Guid.NewGuid(),
                    InstanceOwner = new InstanceOwner
                    {
                        PartyId = "1002"
                    },
                    AppId = Org + "/" + App,
                    Org = Org,
                    Created = DateTime.UtcNow
                },
                new Instance
                {
                    Id = "1000/" + Guid.NewGuid(),
                    InstanceOwner = new InstanceOwner
                    {
                        PartyId = "1000"
                    },
                    AppId = Org + "/" + App,
                    Org = Org,
                    Created = DateTime.UtcNow
                }
            };

            return instances;
        }
    }
}
