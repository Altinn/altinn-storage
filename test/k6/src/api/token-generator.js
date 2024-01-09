import http from "k6/http";
import { check } from "k6";
import encoding from "k6/encoding";

import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";
import * as apiHelpers from "../apiHelpers.js";

const tokenGeneratorUserName = __ENV.tokenGeneratorUserName;
const tokenGeneratorUserPwd = __ENV.tokenGeneratorUserPwd;
const environment = __ENV.env.toLowerCase();

/*
Generate enterprise token for test environment
*/
export function generateEnterpriseToken(queryParams) {
  var endpoint =
    config.tokenGenerator.getEnterpriseToken +
    apiHelpers.buildQueryParametersForEndpoint(queryParams);

  return generateToken(endpoint);
}

export function generatePersonalToken() {

  var userId =  __ENV.userId;
  var partyId = __ENV.partyId;
  var pid = __ENV.pid

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

  return generateToken(endpoint);
}

function generateToken(endpoint) {
  const credentials = `${tokenGeneratorUserName}:${tokenGeneratorUserPwd}`;
  const encodedCredentials = encoding.b64encode(credentials);

  var params = apiHelpers.buildHeaderWithBasic(encodedCredentials);

  var response = http.get(endpoint, params);

  if (response.status != 200) {
    stopIterationOnFail("// TokenGenerator // Token generation failed", false, response);
  }

  var token = response.body;
  return token;
}
