import http from "k6/http";
import encoding from "k6/encoding";

import * as config from "../config.js";
import * as maskinporten from "./maskinporten.js";
import { stopIterationOnFail } from "../errorhandler.js";
import * as apiHelpers from "../apiHelpers.js";

const tokenGeneratorUserName = __ENV.tokenGeneratorUserName;
const tokenGeneratorUserPwd = __ENV.tokenGeneratorUserPwd;
const environment = __ENV.altinn_env.toLowerCase();

const tokenGeneratorPersonalScope = "altinn:testtools/tokengenerator/personal";

export function generateEnterpriseToken(queryParams) {
  var endpoint =
    config.tokenGenerator.getEnterpriseToken +
    apiHelpers.buildQueryParametersForEndpoint(queryParams);
  return getToken(endpoint, basicAuthParams());
}

export function generatePersonalToken() {

  var userId = __ENV.userId;
  var partyId = __ENV.partyId;
  var pid = __ENV.pid;

  if(!userId){
    stopIterationOnFail("Required environment variable user id (userId) was not provided", false);
  }

  if(!partyId){
    stopIterationOnFail("Required environment variable party id (partyId) was not provided", false);
  }

  if(!pid){
    stopIterationOnFail("Required environment variable person number (pid) was not provided", false);
  }

  var queryParams = {
    env: environment,
    userId: userId,
    partyId: partyId,
    pid: pid,
  };

  var endpoint =
    config.tokenGenerator.getPersonalToken +
    apiHelpers.buildQueryParametersForEndpoint(queryParams);

  // tt02 authenticates to the generator via Maskinporten; the AT envs have no
  // Maskinporten configured and keep using Basic auth against the generator.
  if (environment == "tt02") {
    var mpToken = maskinporten.generateAccessToken(tokenGeneratorPersonalScope);
    var header = apiHelpers.buildHeaderWithBearer(mpToken);
    return getToken(endpoint, header);
  }
  return getToken(endpoint, basicAuthParams());
}

function basicAuthParams() {
  const credentials = `${tokenGeneratorUserName}:${tokenGeneratorUserPwd}`;
  return apiHelpers.buildHeaderWithBasic(encoding.b64encode(credentials));
}

function getToken(endpoint, params) {
  var response = http.get(endpoint, params);
  if (response.status != 200) {
    stopIterationOnFail("// Setup // Token generation failed", false, response);
  }
  return response.body;
}