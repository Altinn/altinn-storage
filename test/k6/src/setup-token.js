import { check } from "k6";
import http from "k6/http";
import * as tokenGenerator from "./api/token-generator.js";
import * as maskinporten from "./api/maskinporten.js";
import * as authentication from "./api/authentication.js";
import { b64decode } from "k6/encoding";
import { platformAuthentication } from "./config.js";
import { addErrorCount, stopIterationOnFail } from "./errorhandler.js";

const environment = __ENV.altinn_env.toLowerCase();

/*
 * Logs in an end user via Mockporten (test IDP); returns the runtime token.
 * pid must be a synthetic Tenor fødselsnummer (month 81-92). Never log res.url.
 */
export function AuthenticateWithMockporten() {
  http.cookieJar().clear(platformAuthentication.refresh);
  var endpoint = platformAuthentication.refresh + "&iss=mockporten";
  var res = http.get(endpoint);
  var success = check(res, { "Mockporten login form loaded": (r) => r.status === 200 });
  addErrorCount(success);
  stopIterationOnFail("Mockporten login form not loaded", success, res);

  res = res.submitForm({ fields: { Pid: __ENV.pid, Password: __ENV.testidppwd } });
  success = check(res, { "Mockporten authentication success": (r) => r.status === 200 });
  addErrorCount(success);
  stopIterationOnFail("Mockporten authentication failed", success, res);
  return res.body;
}

/*
 * Generate an altinn token for org based on the environment using AltinnTestTools
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

export function getAltinnClaimFromToken(jwtToken, claimName) {
  const parts = jwtToken.split(".");
  var claims = JSON.parse(b64decode(parts[1].toString(), "rawstd", "s"));

  return claims["urn:altinn:" + claimName];
}
