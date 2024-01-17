import * as tokenGenerator from "./api/token-generator.js";
import * as maskinporten from "./api/maskinporten.js";
import * as authentication from "./api/authentication.js";
import { b64decode } from "k6/encoding";

const environment = __ENV.env.toLowerCase();

/*
 * generate an altinn token for org based on the environment using AltinnTestTools
 * or Maskinporten depending on the environment.
 * If org is not provided TTD will be used.
 * @returns altinn token with the provided scopes for an org
 */
export function getAltinnTokenForOrg(scopes, org = "ttd", orgNo = "991825827") {
  if ((environment == "prod" || environment == "tt02") && org == "ttd") {
    var accessToken = maskinporten.generateAccessToken(scopes);
    return authentication.exchangeToAltinnToken(accessToken, true);
  }

  var queryParams = {
    env: environment,
    scopes: scopes.replace(/ /gi, ","),
    org: org,
    orgNo: orgNo,
  };

  return tokenGenerator.generateEnterpriseToken(queryParams);
}

export function getAltinnTokenForUser() {
  if (environment == "prod" || environment == "tt02") {
    return authentication.authenticateUser();
  }

  return tokenGenerator.generatePersonalToken();
}

export function getAltinnClaimFromToken(jwtToken, claimName) {
  const parts = jwtToken.split(".");
  var claims = JSON.parse(b64decode(parts[1].toString(), "rawstd", "s"));

  return claims["urn:altinn:" + claimName];
}
