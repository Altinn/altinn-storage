using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;

using Altinn.AccessManagement.Core.Models;
using Altinn.Common.AccessToken.Constants;
using Altinn.Platform.Storage.UnitTest.Mocks;

using AltinnCore.Authentication.Constants;

namespace Altinn.Platform.Storage.UnitTest.Utils
{
    public static class PrincipalUtil
    {
        public static readonly string AltinnCoreClaimTypesOrg = "urn:altinn:org";
        public static readonly string AltinnCoreClaimTypesOrgNumber = "urn:altinn:orgNumber";

        public static string GetToken(int userId, int partyId, int authenticationLevel = 2)
        {
            List<Claim> claims = new List<Claim>();
            string issuer = "UnitTest";
            claims.Add(new Claim(AltinnCoreClaimTypes.UserId, userId.ToString(), ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.UserName, "UserOne", ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.PartyID, partyId.ToString(), ClaimValueTypes.Integer32, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticateMethod, "Mock", ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticationLevel, authenticationLevel.ToString(), ClaimValueTypes.Integer32, issuer));

            ClaimsIdentity identity = new ClaimsIdentity("mock");
            identity.AddClaims(claims);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            string token = JwtTokenMock.GenerateToken(principal, new TimeSpan(1, 1, 1));

            return token;
        }

        public static ClaimsPrincipal GetPrincipal(int userId, int partyId, int authenticationLevel = 2)
        {
            List<Claim> claims = new List<Claim>();
            string issuer = "UnitTest";
            claims.Add(new Claim(AltinnCoreClaimTypes.UserId, userId.ToString(), ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.UserName, "UserOne", ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.PartyID, partyId.ToString(), ClaimValueTypes.Integer32, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticateMethod, "Mock", ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticationLevel, authenticationLevel.ToString(), ClaimValueTypes.Integer32, issuer));

            ClaimsIdentity identity = new ClaimsIdentity("mock");
            identity.AddClaims(claims);
            return new ClaimsPrincipal(identity);
        }

        public static string GetOrgToken(string org, int orgNumber = 111111111, string scope = "altinn:appdeploy")
        {
            List<Claim> claims = new List<Claim>();
            string issuer = "UnitTest";
            claims.Add(new Claim(AltinnCoreClaimTypesOrg, org, ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypesOrgNumber, orgNumber.ToString(), ClaimValueTypes.Integer32, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticateMethod, "Mock", ClaimValueTypes.String, issuer));
            claims.Add(new Claim(AltinnCoreClaimTypes.AuthenticationLevel, "3", ClaimValueTypes.Integer32, issuer));
            claims.Add(new Claim("urn:altinn:scope", scope, ClaimValueTypes.String, "maskinporten"));

            ClaimsIdentity identity = new ClaimsIdentity("mock-org");
            identity.AddClaims(claims);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            string token = JwtTokenMock.GenerateToken(principal, new TimeSpan(1, 1, 1));

            return token;
        }

        public static string GetSystemUserToken(string systemUserId, string orgNumber)
        {
            string issuer = "UnitTest";
            SystemUserClaim systemUserClaim = new SystemUserClaim
            {
                Systemuser_id = new List<string>() { systemUserId },
                Systemuser_org = new OrgClaim() { ID = orgNumber },
                System_id = "the_matrix"
            };

            List<Claim> claims = [
                new Claim("authorization_details", JsonSerializer.Serialize(systemUserClaim), "string", issuer), 
                new Claim(AltinnCoreClaimTypes.AuthenticateMethod, "Mock", ClaimValueTypes.String, issuer), 
                new Claim(AltinnCoreClaimTypes.AuthenticationLevel, "3", ClaimValueTypes.Integer32, issuer)];

            ClaimsPrincipal principal = new(new ClaimsIdentity(claims));

            string token = JwtTokenMock.GenerateToken(principal, new TimeSpan(1, 1, 1));

            return token;
        }

        public static string GetAccessToken()
        {
            List<Claim> claims = new List<Claim>();
            string issuer = "platform";

            ClaimsIdentity identity = new ClaimsIdentity("mock-org");
            identity.AddClaims(claims);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            string token = JwtTokenMock.GenerateToken(principal, new TimeSpan(1, 1, 1), issuer);

            return token;
        }

        public static string GetAccessToken(string appId)
        {
            List<Claim> claims = new List<Claim>();
            string issuer = "UnitTest";
            if (!string.IsNullOrEmpty(appId))
            {
                claims.Add(new Claim("urn:altinn:app", appId, ClaimValueTypes.String, issuer));
            }

            ClaimsIdentity identity = new ClaimsIdentity("mock-org");
            identity.AddClaims(claims);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            string token = JwtTokenMock.GenerateToken(principal, new TimeSpan(1, 1, 1), issuer);

            return token;
        }

        public static string GetAccessToken(string issuer, string app)
        {
            List<Claim> claims = new List<Claim>
            {
                new Claim(AccessTokenClaimTypes.App, app, ClaimValueTypes.String, issuer)
            };

            ClaimsIdentity identity = new ClaimsIdentity("mock");
            identity.AddClaims(claims);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);
            string token = JwtTokenMock.GenerateToken(principal, new TimeSpan(0, 1, 5), issuer);

            return token;
        }
    }
}
