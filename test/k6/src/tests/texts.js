/*
    Test script for storage texts api
    Command:
    docker compose run k6 run /src/tests/texts.js `
    -e env=*** `
    -e tokenGeneratorUserName=*** `
    -e tokenGeneratorUserPwd=*** `
    -e app=*** `
    -e runFullTestSet=true

    For use case tests omit environment variable runFullTestSet or set value to false
*/
import { check } from "k6";
import * as setupToken from "../setup-token.js";
import { generateReport } from "../report.js";
import * as textsApi from "../api/texts.js";
import { addErrorCount } from "../errorhandler.js";

export const options = {
  thresholds: {
    errors: ["count<1"],
  },
};

export function setup() {
  var scopes = "altinn:serviceowner";
  const app = __ENV.app.toLowerCase();
  const org = "ttd";
  var token = setupToken.getAltinnTokenForOrg(scopes);

  const runFullTestSet = __ENV.runFullTestSet
    ? __ENV.runFullTestSet.toLowerCase().includes("true")
    : false;

  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    org: org,
    app: app,
  };

  return data;
}

export default function (data) {
  try {
    if (data.runFullTestSet) {
      TC01_GetAppTexts(data);
      TC02_PostNewAppText_Invalid(data);
    } else {
      // Limited test set for use case tests
      TC01_GetAppTexts(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

// 01 - POST new application text as org
function TC01_GetAppTexts(data) {
  var response, success;

  response = textsApi.getTexts(data.token, data.org, data.app, "nb");
  success = check(response, {
    "GET App texts is 200": (r) => r.status === 200,
  });
  addErrorCount(success);
}

// 02 - POST new application text missing APIM subscription key
function TC02_PostNewAppText_Invalid(data) {
  var response, success;

  response = textsApi.postText(
    data.token,
    data.org,
    data.app,
    JSON.stringify(data.texts)
  );

  success = check(response, {
    "POST new app text missing APIM subscription key . Status is 401": (r) =>
      r.status === 401,
  });

  addErrorCount(success);
}

/*
export function handleSummary(data) {
 return generateReport(data, "texts");
}
*/
