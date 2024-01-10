/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/messageboxinstance.js `
    -e env=*** `
    -e userId=*** `
    -e partyId=*** `
    -e pid=*** `
    -e username=*** `
    -e userpwd=*** `
    -e org=ttd `
    -e app=*** `
    -e apimSubsKey=*** `
    -e apimSblSubsKey=*** `
    -e tokenGeneratorUserName=*** `
    -e tokenGeneratorUserPwd=*** `
    -e runFullTestSet=true `
    -e useTestTokenGenerator=true
*/
import { check } from "k6";
import * as cleanup from "../cleanup.js";
import * as setupToken from "../setup-token.js";
import * as setupData from "../setup-data.js";
import * as msgboxApi from "../api/msgboxinstances.js";
import { generateJUnitXML, reportPath } from "../report.js";
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

  const userId = __ENV.userId;
  const partyId = __ENV.partyId;
  const pid = __ENV.pid;
  const username = __ENV.username;
  const userpassword = __ENV.userpwd;

  const org = __ENV.org;
  const app = __ENV.app;

  var token = setupToken.getAltinnTokenForUser(
    userId,
    partyId,
    pid,
    username,
    userpassword
  );

  if (!partyId) {
    partyId = setupToken.getPartyIdFromTokenClaim(userToken);
  }

  const instanceId = setupData.getInstanceForTest(token, partyId, org, app);

  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    partyId: partyId,
    instanceId: instanceId,
    org: org,
    app: app,
  };

  return data;
}

// TC01 - GET instance by id
function TC01_GetInstanceById(data) {
  var res = msgboxApi.getInstanceById(data.token, data.instanceId);
  var instance = JSON.parse(res.body);
  var success = check([res, instance], {
    "TC01_GetInstanceById: Get instance by id. Status is 200": (r) =>
      r[0].status === 200,
    "TC01_GetInstanceById: Get instance by id. InstanceId is a match": (r) =>
      r[1].instanceOwnerId + "/" + r[1].id === data.instanceId,
  });
  addErrorCount(success);
}

//  TC02 - POST search active instances
function TC02_SearchActiveInstances(data) {
  var queryModel = {
    language: "nb",
    appId: data.org + "/" + data.app,
    instanceOwnerPartyIdList: [data.partyId],
    includeActive: "true",
  };
  var res = msgboxApi.searchInstances(data.token, queryModel);

  var instances = JSON.parse(res.body);
  var success = check([res, instances], {
    "TC02_SearchActiveInstances: POST search active instances by appId. Status is 200":
      (r) => r[0].status === 200,
    "TC02_SearchActiveInstances: POST search active instances by appId. Count is more than 0":
      (r) => r[1].length > 0,
    "TC02_SearchActiveInstances: Post search active instances by appId. Only active instances retrieved":
      (r) => {
        return (
          instances.every((instance) => instance.archivedDateTime === null) &&
          instances.every((instance) => instance.deletedDateTime === null)
        );
      },
  });
  addErrorCount(success);
}

//  TC03 - POST search  instances
function TC03_SearchInstances(data) {
  var queryModel = {
    language: "nb",
    instanceOwnerPartyIdList: [data.partyId],
    fromCreated: setupData.todayDateInISO(),
    toLastChanged: new Date().toISOString(),
  };

  var res = msgboxApi.searchInstances(data.token, queryModel);
  var instances = JSON.parse(res.body);

  var success = check([res, instances], {
    "TC03_SearchInstances: POST search instances (created, lastChanged). Status is 200":
      (r) => r[0].status === 200,
    "TC03_SearchInstances: POST search instances (created, lastChanged). Count is more than 0":
      (r) => r[1].length > 0,
    "TC03_SearchInstances: POST search instances (created, lastChanged). Created date is greater than today":
      (r) => {
        return r[1].every(
          (instance) => instance.createdDateTime > setupData.todayDateInISO()
        );
      },
  });
  addErrorCount(success);

  //Test to search instances based on filter parameters: search string (app title) from storage: SBL and validate the response
  queryModel = {
    instanceOwnerPartyIdList: [data.partyId],
    searchString: "storage-end-to-end",
    language: "nb",
  };

  res = msgboxApi.searchInstances(data.token, queryModel);

  var instances = JSON.parse(res.body);
  var success = check([res, instances], {
    "TC03_SearchInstances: POST search instance (app title). Status is 200": (
      r
    ) => r[0].status === 200,
    "TC03_SearchInstances: POST search instances (app title). Count is more than 0":
      (r) => r[1].length > 0,
    "TC03_SearchInstances: POST search instance (app title). App title matches":
      (r) => {
        return r[1].every((instance) =>
          instance.title.includes("storage-end-to-end")
        );
      },
  });
  addErrorCount(success);
}

// TC04 - GET instance events
function TC04_GetInstanceEvents(data) {
  var res = msgboxApi.getInstanceEvents(data.token, data.instanceId);
  var events = JSON.parse(res.body);

  var success = check([res, events], {
    "TC04_GetInstanceEvents: Get instance events. Status is 200": (r) =>
      r[0].status === 200,
    "TC04_GetInstanceEvents: Get instance events. List contains at least 1 element":
      (r) => r[1].length != 0,
    "TC04_GetInstanceEvents: Get instance events. Contains a created event": (
      r
    ) => r[1].some((event) => event.eventType === "Created"),
  });
  addErrorCount(success);
}

// TC05 - DELETE soft delete instance
function TC05_SoftDeleteInstance(data) {
  var res = msgboxApi.deleteInstance(data.token, data.instanceId, false);
  var success = check([res], {
    "TC05_SoftDeleteInstance: Soft delete instance. Status is 200": (r) =>
      r[0].status === 200,
  });
  addErrorCount(success);
}

// TC06 - PUT undelete instance
function TC06_UndeleteInstance(data) {
  var res = msgboxApi.undeleteInstance(data.token, data.instanceId);
  var success = check(res, {
    "TC06_UndeleteInstance: Undelete instance. Status is 200": (r) =>
      r.status === 200,
    "TC06_UndeleteInstance: Response is true": (r) => r.body === "true",
  });
  addErrorCount(success);
}

// TC07 - DELETE hard delete instance
function TC07_HardDeleteInstance(data) {
  var res = msgboxApi.deleteInstance(data.token, data.instanceId, true);
  var success = check([res], {
    "TC07_HardDeleteInstance: Hard delete instance. Status is 200": (r) =>
      r[0].status === 200,
  });
  addErrorCount(success);
}

/*
 * 01 - GET instance by id
 * 02 - POST search active instances
 * 03 - POST search instances
 * 04 - GET instance events
 * 05  DELETE soft delete instance
 * 06 - PUT undelete instance
 * 07 - DELETE hard delete instance
 */
export default function (data) {
  try {
    if (data.runFullTestSet) {
      TC01_GetInstanceById(data);
      TC02_SearchActiveInstances(data);
      TC03_SearchInstances(data);
      TC04_GetInstanceEvents(data);
      TC05_SoftDeleteInstance(data);
      TC06_UndeleteInstance(data);
      TC07_HardDeleteInstance(data);
    } else {
      // Limited test set for use case tests
      TC01_GetInstanceById(data);
      TC02_SearchActiveInstances(data);
      TC04_GetInstanceEvents(data);
      TC07_HardDeleteInstance(data);
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

export function teardown(data) {
  cleanup.hardDeleteInstance(data.token, data.instanceId);
}

/*
export function handleSummary(data) {
  let result = {};
  result[reportPath("events.xml")] = generateJUnitXML(data, "platform-storage-messageboxinstances");
  return result;
}
*/
