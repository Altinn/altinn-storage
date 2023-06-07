import http from "k6/http";
import * as apiHelper from "../apiHelpers.js";
import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";

/**
 * Api call to Storage:Instances to create an app instance and returns response
 * @param {*} token token value to be sent in apiHelper for authentication
 * @param {*} partyId party id of the user to whom instance is to be created
 * @param {*} org short name of ap owner
 * @param {*} app app name
 * @param {JSON} serializedInstance instance json metadata sent in request body
 * @returns {JSON} Json object including response apiHelperss, body, timings
 */
export function postInstance(token, partyId, org, app, serializedInstance) {
  var appId = org + "/" + app;
  var endpoint = config.platformStorage["instances"] + "?appId=" + appId;
  var params = apiHelper.buildHeaderWithBearerAndContentType(
    token,
    "application/json"
  );
  var requestbody = JSON.stringify(
    buildInstanceInputJson(serializedInstance, appId, partyId)
  );
  return http.post(endpoint, requestbody, params);
}

//Function to build input json for creation of instance with app, instanceOwner details and returns a JSON object
function buildInstanceInputJson(instanceJson, appId, partyId) {
  instanceJson = JSON.parse(instanceJson);
  instanceJson.instanceOwner.partyId = partyId;
  instanceJson.appId = appId;
  return instanceJson;
}

//Api call to Storage:Instances to get an instance by id and return response
export function getInstanceById(
  altinnStudioRuntimeCookie,
  partyId,
  instanceId
) {
  var endpoint = config.buildStorageUrls(partyId, instanceId, "", "instanceid");
  var params = apiHelper.buildHeaderWithBearer(
    altinnStudioRuntimeCookie,
    "platform"
  );
  return http.get(endpoint, params);
}

//Api call to Storage:Instances to get all instances under a party id and return response
export function getAllinstancesWithFilters(altinnStudioRuntimeCookie, filters) {
  var endpoint = config.platformStorage["instances"];
  endpoint +=
    filters != null ? apiHelper.buildQueryParametersForEndpoint(filters) : "";
  var params = apiHelper.buildHeaderWithBearer(
    altinnStudioRuntimeCookie,
    "platform"
  );
  return http.get(endpoint, params);
}

//Api call to Storage:Instances to get all archived instances under an app created after a specific date and return response
export function getArchivedInstancesByOrgAndApp(
  altinnStudioRuntimeCookie,
  appOwner,
  appName,
  isArchived,
  createdDateTime
) {
  //If createdDateTime is not sent update the value to today's date
  if (!createdDateTime) {
    createdDateTime = support.todayDateInISO();
  }
  var filters = {
    created: `gt:${createdDateTime}`,
    org: appOwner,
    appId: `${appOwner}/${appName}`,
    "process.isComplete": isArchived,
  };

  //find archived instances of the app that has created date > createdDateTime
  var endpoint =
    config.platformStorage["instances"] +
    support.buildQueryParametersForEndpoint(filters);
  var params = apiHelper.buildHeaderWithBearer(
    altinnStudioRuntimeCookie,
    "platform"
  );
  return http.get(endpoint, params);
}

//Api call to Storage:Instances to soft/hard delete an instance by id and return response
export function deleteInstanceById(
  token,
  instanceId,
  hardDelete
) {
  var endpoint =
    config.buildStorageUrls(instanceId, "", "instanceid") +
    "?hard=" +
    hardDelete;
  var params = apiHelper.buildHeaderWithBearer(
    token,
  );
  return http.del(endpoint, null, params);
}
