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
    -e orgNumber=*** `
    -e app=*** `
    -e apimSubsKey=*** `
    -e tokenGeneratorUserName=*** `
    -e tokenGeneratorUserPwd=*** `
    -e runFullTestSet=true `
    -e useTestTokenGenerator=true `
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

  const org = __ENV.org;
  const app = __ENV.app;

  const userId = __ENV.userId;
  const pid = __ENV.pid;
  const username = __ENV.username;
  const userpassword = __ENV.userpwd;
  let partyId = __ENV.partyId;
  const orgNumber = __ENV.orgNumber;

  var scopes =
    "altinn:serviceowner/instances.read altinn:serviceowner/instances.write";
  var orgToken = setupToken.getAltinnTokenForOrg(scopes);

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

  var data = {
    runFullTestSet: runFullTestSet,
    userToken: userToken,
    orgToken: orgToken,
    partyId: partyId,
    personNumber: pid,
    orgNumber: orgNumber,
    org: org,
    app: app,
  };

  return data;
}

// TC01 - POST instance
function TC01_PostInstance(data) {
  var res = instancesApi.postInstance(
    data.userToken,
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
  var res = instancesApi.getInstanceById(data.userToken, data.instanceId);

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
  var res = instancesApi.getInstances(data.userToken, filters);

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

  var res = instancesApi.getInstances(data.userToken, filters);
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
  var res = instancesApi.putReadStatus(
    data.userToken,
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

  var res = instancesApi.putPresentationTexts(
    data.userToken,
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
  var res = instancesApi.putDataValues(
    data.userToken,
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

function TC08_SetSubStatus(data) {
  var subStatus = {
    label: "This is the label",
    description: "This is the description",
  };

  var res = instancesApi.putSubstatus(
    data.orgToken,
    data.instanceId,
    subStatus
  );
  var instance = res.json();

  var success = check([res, instance.status], {
    "TC08_SetSubStatus: Set sub status. Response is 200": (r) =>
      r[0].status === 200,
    "TC08_SetSubStatus: Set sub status. Instance sub status is updated": (r) =>
      r[1].substatus.label == "This is the label",
  });
  addErrorCount(success);
}

function TC09_CompleteConfirmInstance(data) {
  var res = instancesApi.completeInstance(
    data.orgToken,
    data.instanceId,
    false
  );

  console.log(res);
  var success = check(res, {
    "TC09_CompleteConfirmInstance: Complete confirm instance. Status is 200": (
      r
    ) => r.status === 200,
  });
  addErrorCount(success);
}

function TC10_SoftDeleteInstance(data) {
  var res = instancesApi.deleteInstanceById(
    data.userToken,
    data.instanceId,
    false
  );

  var success = check(res, {
    "TC10_SoftDeleteInstance: Soft delete instance. Status is 200": (r) =>
      r.status === 200,
    "TC10_SoftDeleteInstance: Soft delete instance. Soft DELETE date populated":
      (r) => JSON.parse(r.body).status.softDeleted != null,
  });

  addErrorCount(success);
}

function TC11_HardDeleteInstance(data) {
  var res = instancesApi.deleteInstanceById(
    data.userToken,
    data.instanceId,
    true
  );

  var success = check(res, {
    "TC11_HardDeleteInstance: Hard delete instance. Status is 200": (r) =>
      r.status === 200,
  });

  addErrorCount(success);
}

//TC12 - Get all instances for party looked up with a person number
function TC12_GetInstances_ByPersonNumber(data) {
  var instanceOwnerIdentifier = "Person:" + data.personNumber;
  var res = instancesApi.getInstanceByInstanceOwnerIdentifier(data.userToken, instanceOwnerIdentifier, data.org, data.app);

  var success = check(res, {
    "TC12_GetInstances_ByPersonNumber: Get instance for party. Status is 200": (
      r
    ) => r.status === 200,
  });
  addErrorCount(success);

  const firstInstance = JSON.parse(res.body).instances[0];
  if (firstInstance) { // Making sure there actually exists instances for the instanceOwner
    const instanceIdSplit = firstInstance["id"].split("/");

    success = check(res, {
      "TC12_GetInstances_ByPersonNumber: Get instance for party. InstanceId has expected format":
        instanceIdSplit.length === 2,
    });
    addErrorCount(success);
  }
}

//TC13 - Get all instances for party looked up with an organisation number
function TC13_GetInstances_ByOrgNumber(data) {
  var instanceOwnerIdentifier = "Organisation:" + data.orgNumber;
  var res = instancesApi.getInstanceByInstanceOwnerIdentifier(data.orgToken, instanceOwnerIdentifier, data.org, data.app);

  var success = check(res, {
    "TC13_GetInstances_ByOrgNumber: Get instance for party. Status is 200": (
      r
    ) => r.status === 200,
  });
  addErrorCount(success);

  const firstInstance = JSON.parse(res.body).instances[0];
  if (firstInstance) { // Making sure there actually exists instances for the instanceOwner
    const orgnasationNumber = firstInstance.instanceOwner.organisationNumber;
    const instanceIdSplit = firstInstance["id"].split("/");

    success = check(res, {
      "TC13_GetInstances_ByOrgNumber: Get instance for party. InstanceId has expected format":
        instanceIdSplit.length === 2,
        "TC13_GetInstances_ByOrgNumber: Get instance for party. Organisation number matches instanceOwner.organisationNumber":
        orgnasationNumber === data.orgNumber,
    });
    addErrorCount(success);
  }
}

/*
 * TC01_PostInstance
 * TC02_GetInstanceById
 * TC03_GetInstances_PartyFilter
 * TC04_GetInstances_IsArchivedFilter
 * TC05_SetReadStatus
 * TC06_SetPresentationTexts
 * TC07_SetDataValues
 * TC08_SetSubStatus
 * TC09_CompleteConfirmInstance
 * TC10_SoftDeleteInstance
 * TC11_HardDeleteInstance
 * TC12_GetInstances_ByPersonNumber
 * TC13_GetInstances_ByOrgNumber
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
      TC08_SetSubStatus(data);
      TC09_CompleteConfirmInstance(data);
      TC10_SoftDeleteInstance(data);
      TC11_HardDeleteInstance(data);
      TC12_GetInstances_ByPersonNumber(data);
      TC13_GetInstances_ByOrgNumber(data);
    } else {
      // Limited test set for use case tests
      var instanceId = TC01_PostInstance(data);
      data.instanceId = instanceId;
      TC02_GetInstanceById(data);
      TC03_GetInstances_PartyFilter(data);
      TC11_HardDeleteInstance(data);
      TC12_GetInstances_ByPersonNumber(data);
      TC13_GetInstances_ByOrgNumber(data);
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
