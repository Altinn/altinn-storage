using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Register.Models;
using Altinn.Platform.Storage.Configuration;
using Altinn.Platform.Storage.Exceptions;
using Altinn.Platform.Storage.Extensions;

using AltinnCore.Authentication.Utils;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Storage.Services
{
    /// <summary>
    /// Handles register service
    /// </summary>
    public class RegisterService : IRegisterService
    {
        private readonly HttpClient _client;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly GeneralSettings _generalSettings;
        private readonly IAccessTokenGenerator _accessTokenGenerator;
        private readonly ILogger<IRegisterService> _logger;

        private readonly JsonSerializerOptions _serializerOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="RegisterService"/> class.
        /// </summary>
        public RegisterService(
            HttpClient httpClient,
            IHttpContextAccessor httpContextAccessor,
            IAccessTokenGenerator accessTokenGenerator,
            IOptions<GeneralSettings> generalSettings,
            IOptions<RegisterServiceSettings> registerServiceSettings,
            ILogger<RegisterService> logger)
        {
            httpClient.BaseAddress = new Uri(registerServiceSettings.Value.ApiRegisterEndpoint);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client = httpClient;
            _httpContextAccessor = httpContextAccessor;
            _generalSettings = generalSettings.Value;
            _accessTokenGenerator = accessTokenGenerator;
            _logger = logger;

            _serializerOptions = new()
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        /// <inheritdoc/>
        public async Task<Party> GetParty(int partyId)
        {
            Party party = null;

            string endpointUrl = $"parties/{partyId}";
            string token = JwtTokenUtil.GetTokenFromContext(_httpContextAccessor.HttpContext, _generalSettings.RuntimeCookieName);
            string accessToken = _accessTokenGenerator.GenerateAccessToken("platform", "events");

            HttpResponseMessage response = await _client.GetAsync(token, endpointUrl, accessToken);
            HttpStatusCode responseHttpStatusCode = response.StatusCode;

            if (responseHttpStatusCode == HttpStatusCode.OK)
            {
                party = await response.Content.ReadFromJsonAsync<Party>(_serializerOptions);
            }
            else
            {
                _logger.LogError("// Getting party with partyID {PartyId} failed with statuscode {ResponseHttpStatusCode}", partyId, responseHttpStatusCode);
            }

            return party;
        }

        /// <inheritdoc/>
        public async Task<int> PartyLookup(string person, string orgNo)
        {
            string endpointUrl = "parties/lookup";

            PartyLookup partyLookup = new PartyLookup() { Ssn = person, OrgNo = orgNo };

            string bearerToken = JwtTokenUtil.GetTokenFromContext(_httpContextAccessor.HttpContext, _generalSettings.RuntimeCookieName);
            string accessToken = _accessTokenGenerator.GenerateAccessToken("platform", "storage");

            StringContent content = new StringContent(JsonSerializer.Serialize(partyLookup));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

            HttpResponseMessage response = await _client.PostAsync(bearerToken, endpointUrl, content, accessToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Party party = await response.Content.ReadFromJsonAsync<Party>(_serializerOptions);
                return party.PartyId;
            }
            else
            {
                string reason = await response.Content.ReadAsStringAsync();
                _logger.LogError("// RegisterService // PartyLookup // Failed to lookup party in platform register. Response status code is {StatusCode}. \n Reason {Reason}.", response.StatusCode, reason);

                throw await PlatformHttpException.CreateAsync(response);
            }
        }
    }
}
