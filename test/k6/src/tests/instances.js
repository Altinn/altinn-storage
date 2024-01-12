/*
    Test script for the data endpoint in the Storage API
    Command:
    docker-compose run k6 run /src/tests/instances.js `
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
import * as setupToken from "../setup-token.js";
import * as instancesApi from "../api/instances.js";
import { generateReport } from "../report.js";
import { addErrorCount } from "../errorhandler.js";
let serializedInstance = open("../data/instance.json");

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

  var data = {
    runFullTestSet: runFullTestSet,
    token: token,
    partyId: partyId,
    org: org,
    app: app,
  };

  return data;
}

// TC01 - POST instance
function TC01_PostInstance(data) {
  var res = instancesApi.postInstance(
    data.token,
    data.partyId,
    data.org,
    data.app,
    serializedInstance
  );

  var success = check(res, {
    "TC01_PostInstance: Create new instance. Status is 201": (r) =>
      r.status === 201,
    "TC01_PostInstance: Create new instance. Instance Id is not null": (r) =>
      JSON.parse(r.body).id != null,
  });
  addErrorCount(success);

  return JSON.parse(res.body)["id"];
}

// TC02 - Get instance by id
function TC02_GetInstanceById(data) {
  var res = instancesApi.getInstanceById(data.token, data.instanceId);

  var success = check(res, {
    "TC02_GetInstanceById: Get instance by id. Status is 200": (r) =>
      r.status === 200,
  });
  addErrorCount(success);
}

//TC03 - Get all instances for party
function TC03_GetInstances_PartyFilter(data) {
  var filters = {
    "instanceOwner.partyId": data.partyId,
  };
  var res = instancesApi.getInstances(data.token, filters);

  var success = check(res, {
    "TC03_GetAllInstancesForParty: Get instance for party. Status is 200": (
      r
    ) => r.status === 200,
  });
  addErrorCount(success);

  const firstInstance = JSON.parse(res.body).instances[0];
  const instanceIdSplit = firstInstance["id"].split("/");

  success = check(res, {
    "TC03_GetAllInstancesForParty: Get instance for party. InstanceId has expected format":
      instanceIdSplit.length === 2,
  });
  addErrorCount(success);
}

// TC04 - Get instances with isArchived filter
function TC04_GetInstances_IsArchivedFilter(data) {
  var filters = {
    "instanceOwner.partyId": data.partyId,
    "status.isArchived": true,
    "status.isSoftDeleted": false,
  };

  var res = instancesApi.getInstances(data.token, filters);
  var responseInstances = res.json("instances");

  var success = check(res, {
    "TC04_GetInstances_IsArchivedFilter: Get archived instances. Status is 200":
      (r) => r.status === 200,
    "TC04_GetInstances_IsArchivedFilter: Get archived instances. No instances are hard deleted":
      (r) => {
        return responseInstances.every(
          (instance) => instance.status.isHardDeleted == false
        );
      },
    "TC04_GetInstances_IsArchivedFilter: Get archived instances. All instances are archived":
      (r) => {
        return responseInstances.every(
          (instance) => instance.status.isArchived == true
        );
      },
  });
  addErrorCount(success);
}

//TC05 - Set read status
function TC05_SetReadStatus(data) {
  var res = instancesApi.putUpdateReadStatus(
    data.token,
    data.instanceId,
    "UpdatedSinceLastReview"
  );

  var success = check(res, {
    "TC05_SetReadStatus: Update read status. Response is 200": (r) =>
      r.status === 200,
    "TC05_SetReadStatus: Update read status. Status is updated as read": (r) =>
      r.json().status.readStatus === "UpdatedSinceLastReview",
  });
  addErrorCount(success);
}

//TC06 - Set presentation texts
function TC06_SetPresentationTexts(data) {
  var presentationTexts = {
    texts: {
      text1: "automated",
      text2: "test",
    },
  };

  var res = instancesApi.putUpdatePresentationTexts(
    data.token,
    data.instanceId,
    presentationTexts
  );

  var success = check(res, {
    "TC06_SetPresentationTexts: Update presentation texts. Response is 200": (
      r
    ) => r.status === 200,
    "TC06_SetPresentationTexts: Update presentation texts. Presentation texts are updated in instance metadata":
      (r) => r.json("presentationTexts.text1") == "automated",
  });
  addErrorCount(success);
}

//TC07 - Set data values
function TC07_SetDataValues(data) {
  var dataValues = {
    values: {
      value1: "test",
    },
  };
  var res = instancesApi.putUpdateDataValues(
    data.token,
    data.instanceId,
    dataValues
  );

  var success = check(res, {
    "TC07_SetDataValues: Update data values. Response is 200": (r) =>
      r.status === 200,
    "TC07_SetDataValues: Update data values. Data values are updated in instance metadata":
      (r) => r.json("dataValues.value1") == "test",
  });
  addErrorCount(success);
}

function TC08_SoftDeleteInstance(data) {
  var res = instancesApi.deleteInstanceById(data.token, data.instanceId, false);

  var success = check(res, {
    "TC08_SoftDeleteInstance: Soft delete instance. Status is 200": (r) =>
      r.status === 200,
    "TC08_SoftDeleteInstance: Soft delete instance. Soft DELETE date populated":
      (r) => JSON.parse(r.body).status.softDeleted != null,
  });

  addErrorCount(success);
}

function TC09_HardDeleteInstance(data) {
  var res = instancesApi.deleteInstanceById(data.token, data.instanceId, true);

  var success = check(res, {
    "TC09_HardDeleteInstance: Hard delete instance. Status is 200": (r) =>
      r.status === 200
  });

  addErrorCount(success);
}

/*
 * TC01_PostInstance
 * TC02_GetInstanceById
 * TC03_GetInstances_PartyFilter
 * TC04_GetInstances_IsArchivedFilter
 * TC05_SetReadStatus
 * TC06_SetPresentationTexts
 * TC07_SetDataValues
 * TC08_SoftDeleteInstance
 * TC09_HardDeleteInstance
 */
export default function (data) {
  try {
    if (data.runFullTestSet) {
      var instanceId = TC01_PostInstance(data);
      data.instanceId = instanceId;
      TC02_GetInstanceById(data);
      TC03_GetInstances_PartyFilter(data);
      TC04_GetInstances_IsArchivedFilter(data);
      TC05_SetReadStatus(data);
      TC06_SetPresentationTexts(data);
      TC07_SetDataValues(data);
      TC08_SoftDeleteInstance(data);
      TC09_HardDeleteInstance(data);
    } else {
      // Limited test set for use case tests
    }
  } catch (error) {
    addErrorCount(false);
    throw error;
  }
}

export function teardown(data) {
  if (data.instanceId) {
  }
}

/*
export function handleSummary(data) {
 return generateReport(data, "instances");
}
*/
