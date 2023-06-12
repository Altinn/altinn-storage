/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/sign.js `
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
import { generateJUnitXML, reportPath } from "../report.js";
import * as dataApi from "../api/data.js";
import * as instancesApi from "../api/instances.js";
import * as processApi from "../api/process.js";
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
  pushInstanceToNextStep(token, instanceId);

  var signRequest = {
    signatureDocumentDataType: "signature",
    dataElementSignatures: [
      {
        dataElementId: dataElementId,
        signed: true,
      },
    ],
    signee: {
      userId: tokenClaims["userId"],
    },
  };

  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    partyId: tokenClaims["partyId"],
    userId: tokenClaims["userId"],
    instanceId: instanceId,
    attachmentId: dataElementId,
    signRequest: signRequest,
  };

  return data;
}

function Test_Instance_Sign(data) {
  var res = instancesApi.signInstance(
    data.token,
    data.instanceId,
    data.signRequest
  );

  var success = check(res, {
    "Test_Instance_Sign: Sign instance. Status is 201": (r) => r.status === 201,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Test_Instance_Sign: Sign instance. Failed",
    success,
    res
  );

  res = instancesApi.getInstanceById(data.token, data.instanceId);

  var dataElements = JSON.parse(res.body)["data"];
  success = check([res, dataElements], {
    "Test_Instance_Sign: Get instance. Status is 200": (r) =>
      r[0].status === 200,
    "Test_Instance_Sign: Get instance. Data list contains sign document": (r) =>
      r[1].some((e) => e.dataType === "signature"),
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Test_Instance_Sign: Get instance. Failed",
    success,
    res
  );

  var dataElement = dataElements.find((obj) => {
    return obj.dataType === "signature";
  });

  res = dataApi.getDataFromSelfLink(data.token, dataElement.selfLinks.platform);
  var retrievedSignDocument = JSON.parse(res.body);
  success = check([res, retrievedSignDocument], {
    "Test_Instance_Sign: Get signature document. Status is 200": (r) =>
      r[0].status === 200,
    "Test_Instance_Sign: Get signature document. Validate properties": (r) =>
      r[1].dataElementSignatures.length == 1 &&
      r[1].dataElementSignatures[0].dataElementId == data.attachmentId &&
      r[1].dataElementSignatures[0].sha256Hash ==
        "6f117268b5175eae239ba68d3cc3651280cde60f3d2ab49386a15e353660e2d3" &&
      r[1].signeeInfo.userId == data.userId,
  });
  addErrorCount(success);
}

export default function (data) {
  try {
    if (data.runFullTestSet) {
      Test_Instance_Sign(data);
    } else {
      // Limited test set for use case tests
      Test_Instance_Sign(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
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

function pushInstanceToNextStep(token, instanceId) {
  var process = {
    started: "2023-06-09T10:59:42.653Z",
    startEvent: "string",
    currentTask: {
      flow: 2,
      started: "2023-06-10T10:59:42.653Z",
      elementId: "Task_2",
      name: "Signering",
      altinnTaskType: "signing",
      flowType: "CompleteCurrentMoveToNext",
    },
  };

  var res = processApi.putProcess(token, instanceId, JSON.stringify(process));

  var success = check(res, {
    "// Setup // Push process to next task. Status is 200": (r) =>
      r.status === 200,
  });

  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Push process to next task. Failed",
    success,
    res
  );
}

export function teardown(data) {
  var res = instancesApi.deleteInstanceById(data.token, data.instanceId, true);

  check(res, {
    "// Teardown // Delete instance. Status is 200": (r) => r.status === 200,
  });
}

/*
export function handleSummary(data) {
  let result = {};
  result[reportPath("events.xml")] = generateJUnitXML(data, "platform-storage");
  return result;
}
*/
