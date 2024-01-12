import { check } from "k6";
import * as dataApi from "./api/data.js";
import * as instancesApi from "./api/instances.js";
import * as processApi from "./api/process.js";
import { addErrorCount, stopIterationOnFail } from "./errorhandler.js";
let serializedInstance = open("./data/instance.json");
const processJson = JSON.parse(open("./data/process-task2.json"));

let pdfAttachment = open("./data/apps-test.pdf", "b");

export function getInstanceForTest(token, partyId, org, app) {
  var res = instancesApi.postInstance(
    token,
    partyId,
    org,
    app,
    serializedInstance
  );

  var success = check(res, {
    "// Setup // Generating instance for test": (r) => r.status === 201,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Generating instance for test Failed",
    success,
    res
  );
  return JSON.parse(res.body)["id"];
}

export function addPdfAttachmentToInstance(token, instanceId) {

  var res = dataApi.postData(
    token,
    instanceId,
    pdfAttachment,
    "pdf",
    "attachment"
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

export function pushInstanceToTask2_Signing(token, instanceId, taskType) {
  var processState = processJson;
  processState.altinnTaskType = taskType;
  processState.name="Automated-Test-" + taskType;

  var res = processApi.putProcess(token, instanceId, JSON.stringify(processState));

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

export function todayDateInISO() {
  var todayDateTime = new Date();
  todayDateTime.setUTCHours(0, 0, 0, 0);
  return todayDateTime.toISOString();
}