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
import * as cleanup from "../cleanup.js";
import { generateReport } from "../report.js";
import * as dataApi from "../api/data.js";
import * as instancesApi from "../api/instances.js";
import { addErrorCount, stopIterationOnFail } from "../errorhandler.js";

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

  const pid = __ENV.pid;
  const username = __ENV.username;
  const userpassword = __ENV.userpwd;
  let partyId = __ENV.partyId;
  let userId = __ENV.userId;

  var userToken = setupToken.getAltinnTokenForUser(
    userId,
    partyId,
    pid,
    username,
    userpassword
  );

  if (!partyId) {
    partyId = setupToken.getAltinnClaimFromToken(userToken, "partyid");
    userId = setupToken.getAltinnClaimFromToken(userToken, "userid");
  }

  const instanceId = setupData.getInstanceForTest(userToken, partyId, org, app);

  const dataElementId = setupData.addPdfAttachmentToInstance(
    userToken,
    instanceId
  );
  setupData.pushInstanceToTask2_Signing(userToken, instanceId, "signing");

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
    userToken: userToken,
    partyId: partyId,
    userId: userId,
    instanceId: instanceId,
    attachmentId: dataElementId,
    signRequest: signRequest,
  };

  return data;
}

function TC01_SignInstance(data) {
  var res = instancesApi.signInstance(
    data.userToken,
    data.instanceId,
    data.signRequest
  );

  var success = check(res, {
    "TC01_SignInstance: Sign instance. Status is 201": (r) => r.status === 201,
  });

  addErrorCount(success);
  stopIterationOnFail(
    "TC01_SignInstance: Sign instance. Failed",
    success,
    res
  );

  res = instancesApi.getInstanceById(data.userToken, data.instanceId);
  var dataElements = JSON.parse(res.body)["data"];
  success = check([res, dataElements], {
    "TC01_SignInstance: Get instance. Status is 200": (r) =>
      r[0].status === 200,
    "TC01_SignInstance: Get instance. Data list contains sign document": (r) =>
      r[1].some((e) => e.dataType === "signature"),
  });
  addErrorCount(success);
  stopIterationOnFail(
    "TC01_SignInstance: Get instance and validate signature data element. Failed",
    success,
    res
  );

  var dataElement = dataElements.find((obj) => {
    return obj.dataType === "signature";
  });

  res = dataApi.getDataFromSelfLink(
    data.userToken,
    dataElement.selfLinks.platform
  );
  var retrievedSignDocument = JSON.parse(res.body);
  success = check([res, retrievedSignDocument], {
    "TC01_SignInstance: Get signature document. Status is 200": (r) =>
      r[0].status === 200,
    "TC01_SignInstance: Get signature document. Validate properties": (r) =>
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
      TC01_SignInstance(data);
    } else {
      // Limited test set for use case tests
      TC01_SignInstance(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

export function teardown(data) {
  cleanup.hardDeleteInstance(data.userToken, data.instanceId);
}

/*export function handleSummary(data) {
 return generateReport(data, "sign");
}
*/
