/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/process.js `
    -e env=*** `
    -e userId=*** `
    -e partyId=*** `
    -e pid=*** `
    -e username=*** `
    -e userpwd=*** `
    -e org=ttd `
    -e app=*** `
    -e apimSubsKey=*** `
    -e tokenGeneratorUserName=*** `
    -e tokenGeneratorUserPwd=*** `
    -e runFullTestSet=true `
    -e useTestTokenGenerator=true
*/

import { check } from "k6";
import * as cleanup from "../cleanup.js";
import * as setupToken from "../setup-token.js";
import * as setupData from "../setup-data.js";
import { generateReport } from "../report.js";
import * as processApi from "../api/process.js";
import { addErrorCount } from "../errorhandler.js";
let serializedProcessState = open("../data/process-task2.json");

export const options = {
  thresholds: {
    errors: ["count<1"],
  },
};

export function setup() {
  const runFullTestSet = __ENV.runFullTestSet
    ? __ENV.runFullTestSet.toLowerCase().includes("true")
    : false;

  const org = __ENV.org;
  const app = __ENV.app;

  const userId = __ENV.userId;
  const pid = __ENV.pid;
  const username = __ENV.username;
  const userpassword = __ENV.userpwd;
  let partyId = __ENV.partyId;

  var userToken = setupToken.getAltinnTokenForUser(
    userId,
    partyId,
    pid,
    username,
    userpassword
  );

  if (!partyId) {
    partyId = setupToken.getPartyIdFromTokenClaim(userToken);
  }

  const instanceId = setupData.getInstanceForTest(userToken, partyId, org, app);

  var data = {
    runFullTestSet: runFullTestSet,
    userToken: userToken,
    partyId: partyId,
    instanceId: instanceId,
  };

  return data;
}

// 01 - PUT process with valid token
function TC01_PutProcess(data) {
  var response, success;
  response = processApi.putProcess(
    data.userToken,
    data.instanceId,
    serializedProcessState
  );
  var instance = JSON.parse(response.body);

  success = check([response, instance], {
    "TC01_PutProcess: Put new process state for instance. Status is 200": (r) =>
      r[0].status === 200,
    "TC01_PutProcess: Put new process state for instance. Current task name is a match.":
      (r) => r[1].process.currentTask.name === "Automatisert-Test-Signering",
  });
  addErrorCount(success);
}

// 02 - GET process history with valid token
function TC02_GetProcessHistory(data) {
  var response, success;
  response = processApi.getProcessHistory(data.userToken, data.instanceId);
  var processHistory = JSON.parse(response.body);
  success = check([response, processHistory], {
    "TC02_GetProcessHistory: Get process history for instance. Status is 200": (
      r
    ) => r[0].status === 200,
  });
  addErrorCount(success);
}

/*
 * 01 - PUT process with valid token
 * 02 - GET process history with valid token
 */
export default function (data) {
  try {
    if (data.runFullTestSet) {
      TC01_PutProcess(data);
      TC02_GetProcessHistory(data);
    } else {
      // Limited test set for use case tests
      TC01_PutProcess(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

export function teardown(data) {
  cleanup.hardDeleteInstance(data.userToken, data.instanceId);
}

/*
export function handleSummary(data) {
 return generateReport(data, "process");
}
*/
