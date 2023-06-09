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
  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    partyId: tokenClaims["partyId"],
    instanceId: instanceId,
  };

  return data;
}

function Test_Data_References(data) {
  var queryParams = {
    dataType: "attachment",
    generatedFrom: [uuidv4(), uuidv4()],
  };

  var res = dataApi.postData(
    data.token,
    data.instanceId,
    pdfAttachment,
    "pdf",
    queryParams
  );

  var dataElement = JSON.parse(res.body);
  var success = check([res, dataElement.references], {
    "Test_Data_References: Post data with generated from refs. Status is 201": (
      r
    ) => r[0].status === 201,
    "Test_Data_References: Post data with generated from refs. Verify number of references":
      (r) => r[1].length === 2,
  });
  addErrorCount(success);

  res = dataApi.putData(
    data.token,
    data.instanceId,
    dataElement["id"],
    pdfAttachment,
    "pdf",
    queryParams
  );
  dataElement = JSON.parse(res.body);

  success = check([res, dataElement.references], {
    "Test_Data_References: Put on existing data element. Status is 200": (r) =>
      r[0].status === 200,
    "Test_Data_References: Put on existing data element. No references are persisted":
      (r) => r[1] === undefined,
  });
  addErrorCount(success);
}

function Test_Data_Lock(data) {
  var queryParams = {
    dataType: "attachment",
  };

  var res = dataApi.postData(
    data.token,
    data.instanceId,
    pdfAttachment,
    "pdf",
    queryParams
  );
  var dataElement = JSON.parse(res.body);
  var success = check([res, dataElement], {
    "Test_Data_Lock: Generate data element for test case. Status is 201": (r) =>
      r[0].status === 201,
    "Test_Data_Lock: Generate data element for test case. Lock property is false":
      (r) => r[1]["locked"] === false,
  });
  addErrorCount(success);

  res = dataApi.lockData(data.token, data.instanceId, dataElement["id"]);
  dataElement = JSON.parse(res.body);
  success = check([res, dataElement], {
    "Test_Data_Lock: Lock data element first time. Status is 201": (r) =>
      r[0].status === 201,
    "Test_Data_Lock: Lock data element first time. Lock property is true": (
      r
    ) => r[1]["locked"] === true,
  });
  addErrorCount(success);

  res = dataApi.lockData(data.token, data.instanceId, dataElement["id"]);
  dataElement = JSON.parse(res.body);
  success = check([res, dataElement], {
    "Test_Data_Lock: Re-lock locked data element. Status is 200": (r) =>
      r[0].status === 200,
    "Test_Data_Lock: Re-lock locked data element. Lock property is true": (r) =>
      r[1]["locked"] === true,
  });
  addErrorCount(success);

  res = dataApi.unlockData(data.token, data.instanceId, dataElement["id"]);
  dataElement = JSON.parse(res.body);
  success = check([res, dataElement], {
    "Test_Data_Lock: Unlock locked data element. Status is 200": (r) =>
      r[0].status === 200,
    "Test_Data_Lock: Unlock locked data element. Lock property is false": (r) =>
      r[1]["locked"] === false,
  });
  addErrorCount(success);
}

export default function (data) {
  try {
    if (data.runFullTestSet) {
      Test_Data_References(data);
      Test_Data_Lock(data);
    } else {
      // Limited test set for use case tests
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

export function teardown(data) {
  var res = instancesApi.deleteInstanceById(data.token, data.instanceId, true);

  if (res.status != 200) {
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
    "// Setup // Generating instance for test Success": (r) => r.status === 201,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Generating instance for test Failed",
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
