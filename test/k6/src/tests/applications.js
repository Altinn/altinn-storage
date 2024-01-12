/*
    Test script for the applications endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/applications.js `
    -e env=*** `
    -e org=ttd `
    -e app=*** `
    -e runFullTestSet=true `
*/

import { check } from "k6";
import { generateReport } from "../report.js";
import * as applicationsApi from "../api/applications.js";
import { addErrorCount } from "../errorhandler.js";

export const options = {
  thresholds: {
    errors: ["count<1"],
  },
};

export function setup() {
  const runFullTestSet = __ENV.runFullTestSet
    ? __ENV.runFullTestSet.toLowerCase().includes("true")
    : false;

  var data = {
    runFullTestSet: runFullTestSet,
    org: __ENV.org,
    app: __ENV.app,
  };

  return data;
}

// TC01 POST form data
function TC01_GetAllAppsForOrg(data) {
  var res = applicationsApi.getAppsForOrg(
  data.org
  );

  var success = check(res, {
    "TC01_GetAllAppsForOrg: Get all apps. Status is 200": (r) =>
      r.status === 200,
      "TC01_GetAllAppsForOrg: Get all apps. Count > 0": (r) =>
      r.json().applications.length > 0

  });
  addErrorCount(success);
}

// TC02_GetApp
function TC02_GetApp(data) {
  var res = applicationsApi.getApp(
  data.org, data.app
  );

  var success = check(res, {
    "TC02_GetApp: Get apps. Status is 200": (r) =>
      r.status === 200,
      "TC02_GetApp: Get apps. Count > 0": (r) =>
      r.json().id == data.org + "/" + data.app

  });
  addErrorCount(success);
}

/*
 * TC01_GetAllAppsForOrg
 * TC02_GetApp
 */
export default function (data) {
  try {
    if (data.runFullTestSet) {
      TC01_GetAllAppsForOrg(data);
      TC02_GetApp(data);
    } else {
      // Limited test set for use case tests
      TC02_GetApp(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

/*
export function handleSummary(data) {
 return generateReport(data, "data");
}
*/
