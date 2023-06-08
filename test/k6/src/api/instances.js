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

//Api call to Storage:Instances to soft/hard delete an instance by id and return response
export function deleteInstanceById(token, instanceId, hardDelete) {
  var endpoint =
    config.buildStorageUrls(instanceId, "", "instanceid") +
    "?hard=" +
    hardDelete;
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.del(endpoint, null, params);
}

//Api call to Storage:Instances to sign data elements on an instance and return response
export function signInstance(token, instanceId, signRequest) {
  var endpoint =
    config.buildStorageUrls(instanceId, "", "instanceid") + "/sign";
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.post(endpoint, JSON.stringify(signRequest), params);
}

//Function to build input json for creation of instance with app, instanceOwner details and returns a JSON object
function buildInstanceInputJson(instanceJson, appId, partyId) {
  instanceJson = JSON.parse(instanceJson);
  instanceJson.instanceOwner.partyId = partyId;
  instanceJson.appId = appId;
  return instanceJson;
}
