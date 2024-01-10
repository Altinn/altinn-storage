/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/sign.js `
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
    -e useTestTokenGenerator=true/false
*/

import { check } from "k6";
import * as setupToken from "../setup-token.js";
import * as setupData from "../setup-data.js";
import { generateJUnitXML, reportPath } from "../report.js";
import * as dataApi from "../api/data.js";
import * as instancesApi from "../api/instances.js";
import * as processApi from "../api/process.js";
import { addErrorCount, stopIterationOnFail } from "../errorhandler.js";
let serializedProcessState = open("../data/process-task2.json");
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

  const userId = __ENV.userId;
  const partyId = __ENV.partyId;
  const pid = __ENV.pid;
  const username = __ENV.username;
  const userpassword = __ENV.userpwd;

  const org = __ENV.org;
  const app = __ENV.app;

  var token = setupToken.getAltinnTokenForUser(userId, partyId, pid, username, userpassword);
  if (!partyId) {
    partyId = setupToken.getPartyIdFromTokenClaim(userToken);
  }

  const instanceId = setupData.getInstanceForTest(
    token,
    partyId,
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
      userId: userId,
    },
  };

  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    partyId: partyId,
    userId: userId,
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
    "Test_Instance_Sign: Sign instance. Failed",
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
    "Test_Instance_Sign: Get instance and validate signature data element. Failed",
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
  var res = processApi.putProcess(token, instanceId, serializedProcessState);

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
