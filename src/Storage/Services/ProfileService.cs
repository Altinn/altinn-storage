using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Profile.Models;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Extensions;
using AltinnCore.Authentication.Utils;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Client service for profile
    /// </summary>
    public class ProfileService : IProfileService
    {
        private readonly ILogger _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly HttpClient _client;
        private readonly IAccessTokenGenerator _accessTokenGenerator;
        private readonly GeneralSettings _generalSettings;
        private readonly JsonSerializerOptions _serializerOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProfileService"/> class
        /// </summary>
        public ProfileService(
            IOptions<PlatformSettings> platformSettings,
            ILogger<ProfileService> logger,
            IHttpContextAccessor httpContextAccessor,
            HttpClient httpClient,
            IAccessTokenGenerator accessTokenGenerator,
            IOptions<GeneralSettings> generalSettings)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
            httpClient.BaseAddress = new Uri(platformSettings.Value.ApiProfileEndpoint);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client = httpClient;
            _accessTokenGenerator = accessTokenGenerator;
            _generalSettings = generalSettings.Value;

            _serializerOptions = new()
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        /// <inheritdoc />
        public async Task<UserProfile> GetUserProfile(int userId)
        {
            UserProfile userProfile = null;

            string endpointUrl = $"users/{userId}";
            string token = JwtTokenUtil.GetTokenFromContext(_httpContextAccessor.HttpContext, _generalSettings.RuntimeCookieName);
            string accessToken = _accessTokenGenerator.GenerateAccessToken("platform", "storage");

            HttpResponseMessage response = await _client.GetAsync(token, endpointUrl, accessToken);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                userProfile = await response.Content.ReadFromJsonAsync<UserProfile>(_serializerOptions);
            }
            else
            {
                _logger.LogError("Getting user profile with userId {userId} failed with statuscode {response.StatusCode}", userId, response.StatusCode);
            }

            return userProfile;
        }
    }
}
