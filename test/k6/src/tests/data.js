/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/data.js `
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
import * as dataApi from "../api/data.js";
import { addErrorCount } from "../errorhandler.js";
let pdfAttachment = open("../data/apps-test.pdf", "b");
let formDataXml = open("../data/" + __ENV.app + ".xml");

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
  const pid = __ENV.pid;
  const username = __ENV.username;
  const userpassword = __ENV.userpwd;
  let partyId = __ENV.partyId;

  const org = __ENV.org;
  const app = __ENV.app;

  var userToken = setupToken.getAltinnTokenForUser(
    userId,
    partyId,
    pid,
    username,
    userpassword
  );

  if (!partyId) {
    partyId = setupToken.getAltinnClaimFromToken(userToken, "partyid");
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

// TC01 POST form data
function TC01_CreateFormData(data) {
  var res = dataApi.postData(
    data.userToken,
    data.instanceId,
    formDataXml,
    "xml",
    "correspondence"
  );

  var dataElement = res.json();

  var success = check(res, {
    "TC01_CreateFormData: Create form data. Status is 201": (r) =>
      r.status === 201,
    "TC01_CreateFormData: Create form data. Data Id is not null": (r) =>
      dataElement.id != null,
  });
  addErrorCount(success);

  return dataElement.id;
}

// TC02 GET form data by id
function TC02_GetFormDataById(data) {
  var res = dataApi.getDataById(
    data.userToken,
    data.instanceId,
    data.formDataElementId
  );
  var success = check(res, {
    "TC02_GetFormDataById: Get form data by id. Status is 200": (r) =>
      r.status === 200,
  });

  addErrorCount(success);
}

// TC03 PUT form data
function TC03_UpdateFormData(data) {
  var res = dataApi.putData(
    data.userToken,
    data.instanceId,
    data.formDataElementId,
    formDataXml,
    "xml"
  );

  var success = check(res, {
    "TC03_UpdateFormData: Update form data. Status is 200": (r) =>
      r.status === 200,
  });

  addErrorCount(success);
}

// TC04 POST binary data
function TC04_AddPdfAttachment(data) {
  var res = dataApi.postData(
    data.userToken,
    data.instanceId,
    pdfAttachment,
    "pdf",
    "attachment"
  );

  var dataElement = JSON.parse(res.body);
  var success = check([res, dataElement], {
    "TC04_AddPdfAttachment: Upload attachment. Status is 201": (r) =>
      r[0].status === 201,
    "TC04_AddPdfAttachment: Upload attachment. Data Id is not null": (r) =>
      r[1].id != null,
  });
  addErrorCount(success);

  return dataElement.id;
}

// TC05 GET all data for instance
function TC05_GetAllDataForInstance(data) {
  var res = dataApi.getAllDataElements(data.userToken, data.instanceId);

  var success = check(res, {
    "TC05_GetAllDataForInstance. Get all data elements for instance. Status is 200":
      (r) => r.status === 200,
    "TC05_GetAllDataForInstance. Get all data elements for instance. Count is 2":
      (r) => JSON.parse(r.body).dataElements.length === 2,
  });
  addErrorCount(success);
}

// TC06 DELETE binary data
function TC06_DeleteAttachment(data) {
  var res = dataApi.deleteData(
    data.userToken,
    data.instanceId,
    data.attachmentDataElementId
  );

  var success = check(res, {
    "TC06_DeleteAttachment: DELETE Attachment. Data status is 200": (r) =>
      r.status === 200,
  });
  addErrorCount(success);
}

// TC07 Lock and unlock data element
function TC07_LockUnlockDataElement(data) {
  var res = dataApi.lockData(
    data.userToken,
    data.instanceId,
    data.formDataElementId
  );
  var dataElement = res.json();

  var success = check([res, dataElement], {
    "TC07_LockUnlockDataElement: Lock data element first time. Status is 201": (
      r
    ) => r[0].status === 201,
    "TC07_LockUnlockDataElement: Lock data element first time. Lock property is true":
      (r) => r[1]["locked"] === true,
  });
  addErrorCount(success);

  res = dataApi.lockData(
    data.userToken,
    data.instanceId,
    data.formDataElementId
  );
  dataElement = JSON.parse(res.body);
  success = check([res, dataElement], {
    "TC07_LockUnlockDataElement: Re-lock locked data element. Status is 200": (
      r
    ) => r[0].status === 200,
    "TC07_LockUnlockDataElement: Re-lock locked data element. Lock property is true":
      (r) => r[1]["locked"] === true,
  });
  addErrorCount(success);

  res = dataApi.unlockData(data.userToken, data.instanceId, dataElement["id"]);
  dataElement = JSON.parse(res.body);
  success = check([res, dataElement], {
    "TC07_LockUnlockDataElement: Unlock locked data element. Status is 200": (
      r
    ) => r[0].status === 200,
    "TC07_LockUnlockDataElement: Unlock locked data element. Lock property is false":
      (r) => r[1]["locked"] === false,
  });
  addErrorCount(success);
}

function TC08_SetDataReferences(data) {
  var tags = {
    generatedFromTask: ["Task_1"],
  };

  var res = dataApi.postData(
    data.userToken,
    data.instanceId,
    pdfAttachment,
    "pdf",
    "attachment",
    tags
  );

  var dataElement = JSON.parse(res.body);
  var success = check([res, dataElement.references], {
    "TC08_SetDataReferences: Post data with generated from refs. Status is 201":
      (r) => r[0].status === 201,
    "TC08_SetDataReferences: Post data with generated from refs. Verify number of references":
      (r) => r[1].length === 1,
  });
  addErrorCount(success);

  res = dataApi.putData(
    data.userToken,
    data.instanceId,
    dataElement["id"],
    pdfAttachment,
    "pdf",
    tags
  );
  dataElement = JSON.parse(res.body);

  success = check([res, dataElement.references], {
    "TC08_SetDataReferences: Put on existing data element. Status is 200": (
      r
    ) => r[0].status === 200,
    "TC08_SetDataReferences: Put on existing data element. No references are persisted":
      (r) => r[1] === undefined,
  });
  addErrorCount(success);
}

/*
 * TC01_CreateFormData
 * TC02_GetFormDataById
 * TC03_UpdateFormData
 * TC04_AddPdfAttachment
 * TC05_GetAllDataForInstance
 * TC06_DeleteAttachment
 * TC07_LockUnlockDataElement
 * TC08_SetDataReferences
 */
export default function (data) {
  try {
    if (data.runFullTestSet) {
      var formDataElementId = TC01_CreateFormData(data);
      data.formDataElementId = formDataElementId;
      TC02_GetFormDataById(data);
      TC03_UpdateFormData(data);
      var attachmentDataElementId = TC04_AddPdfAttachment(data);
      data.attachmentDataElementId = attachmentDataElementId;

      TC05_GetAllDataForInstance(data);
      TC06_DeleteAttachment(data);
      TC07_LockUnlockDataElement(data);
      TC08_SetDataReferences(data);
    } else {
      // Limited test set for use case tests
      var formDataElementId = TC01_CreateFormData(data);
      data.formDataElementId = formDataElementId;
      TC02_GetFormDataById(data);
      TC03_UpdateFormData(data);
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
 return generateReport(data, "data");
}
*/
