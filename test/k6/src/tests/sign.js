/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/data.js `
    -e env=*** `
    -e username=*** `
    -e userpwd=*** `
    -e org=ttd `
    -e app=*** `
    -e apimSubsKey=*** `
    -e runFullTestSet=true
*/

import { check } from "k6";
import * as setupToken from "../setup.js";
import { uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

import { generateJUnitXML, reportPath } from "../report.js";
import * as dataApi from "../api/data.js";
import * as instancesApi from "../api/instances.js";
import { addErrorCount, stopIterationOnFail } from "../errorhandler.js";
let serializedInstance = open("../data/instance.json");
let pdfAttachment = open("../data/apps-test.pdf", "b");

export const options = {
  thresholds: {
    errors: ["count<1"],
  },
};

export function setup() {
  const runFullTestSet = __ENV.runFullTestSet
    ? __ENV.runFullTestSet.toLowerCase().includes("true")
    : false;

  const userName = __ENV.username;
  const userPassword = __ENV.userpwd;
  const org = __ENV.org;
  const app = __ENV.app;

  var aspxauthCookie = setupToken.authenticateUser(userName, userPassword);
  var token = setupToken.getAltinnStudioRuntimeToken(aspxauthCookie);

  var tokenClaims = setupToken.getTokenClaims(token);

  const instanceId = setupInstanceForTest(
    token,
    tokenClaims["partyId"],
    org,
    app
  );

  const dataElementId = setupAttachmentsForTest(token, instanceId);

  var signRequest = {
    signatureDocumentDataType: "signature",
    dataElementSignatures: [
      {
        dataElementId: dataElementId,
        signed: true,
      },
    ],
    signee: {
      userId: tokenClaims["userid"],
    },
  };

  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    partyId: tokenClaims["partyId"],
    instanceId: instanceId,
    attachmentId: dataElementId,
    org: org,
    app: app,
    signRequest: signRequest
  };

  return data;
}

function Test_Sign_PostSignature(data) {
var res = instancesApi.signInstance(data.token, data.instanceId,data.signRequest);
console.log(res);
}

export default function (data) {
  try {
    if (data.runFullTestSet) {
      Test_Sign_PostSignature(data);
    } else {
      // Limited test set for use case tests
      Test_Sign_PostSignature(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

export function teardown(data) {
  var res = instancesApi.deleteInstanceById(data.token, data.instanceId, true);

  if (res.status == 200) {
    console.log("teardown succeed");
  } else {
    console.log("teardown failed");
  }
}

function setupInstanceForTest(token, partyId) {
  var res = instancesApi.postInstance(
    token,
    partyId,
    __ENV.org,
    __ENV.app,
    serializedInstance
  );

  var success = check(res, {
    "// Setup // Generating instance for test. Success": (r) =>
      r.status === 201,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Generating instance for test. Failed",
    success,
    res
  );

  return JSON.parse(res.body)["id"];
}

function setupAttachmentsForTest(token, instanceId) {
  var queryParams = {
    dataType: "attachment",
  };

  var res = dataApi.postData(
    token,
    instanceId,
    pdfAttachment,
    "pdf",
    queryParams
  );

  var success = check(res, {
    "// Setup // Generate data element for test case. Status is 201": (r) =>
      r.status === 201,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Generate data element for test case. Failed",
    success,
    res
  );

  return JSON.parse(res.body)["id"];
}
/*
export function handleSummary(data) {
  let result = {};
  result[reportPath("events.xml")] = generateJUnitXML(data, "platform-storage");
  return result;
}
*/
