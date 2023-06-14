import http from "k6/http";
import { check } from "k6";
import encoding from "k6/encoding";

import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";
import * as apiHelpers from "../apiHelpers.js";

const tokenGeneratorUserName = __ENV.tokenGeneratorUserName;
const tokenGeneratorUserPwd = __ENV.tokenGeneratorUserPwd;

/*
Generate enterprise token for test environment
*/
export function generateEnterpriseToken(queryParams) {
  var endpoint =
    config.tokenGenerator.getEnterpriseToken +
    apiHelpers.buildQueryParametersForEndpoint(queryParams);

  return generateToken(endpoint);
}

export function generatePersonalToken(queryParams) {
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
    stopIterationOnFail("// Setup // Token generation failed", false, response);
  }

  var token = response.body;
  return token;
}
