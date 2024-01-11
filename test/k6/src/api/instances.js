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
export function getInstanceById(token, instanceId) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "instanceid");
  var params = apiHelper.buildHeaderWithBearer(token, "platform");
  return http.get(endpoint, params);
}
//Api call to Storage:Instances to get instances based on filter parameters and return response
export function getInstances(token, filters) {
  var endpoint = config.platformStorage["instances"];
  endpoint += apiHelper.buildQueryParametersForEndpoint(filters);
  var params = apiHelper.buildHeaderWithBearer(token, "platform");
  return http.get(endpoint, params);
}

//Api call to Storage:Instances to soft/hard delete an instance by id and return response
export function deleteInstanceById(token, instanceId, hardDelete) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "instanceid") + "?hard=" + hardDelete;
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.del(endpoint, null, params);
}

//Api call to Storage:Instances to sign data elements on an instance and return response
export function signInstance(token, instanceId, signRequest) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "sign");
  var params = apiHelper.buildHeaderWithBearerAndContentType(
    token,
    "application/json"
  );

  return http.post(endpoint, JSON.stringify(signRequest), params);
}

//Api call to Storage:Instances to complete confirm an instance and return response
export function completeInstance(token, instanceId) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "complete");
  var params = apiHelper.buildHeaderWithBearer(token);

  return http.post(endpoint, params);
}

//Api call to Storage:Instances to update the read status to: Unread, Read, UpdatedSinceLastReview
//an instance by id and return response
export function putUpdateReadStatus(token, instanceId, readStatus) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "readstatus") + "?status=" + readStatus;
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.put(endpoint, null, params);
}

//Api call to Storage:Instances to update the presentation texts for an instance by id and return response
export function putUpdatePresentationTexts(token, instanceId, presentationTexts) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "presentationtexts");
  var params = apiHelper.buildHeaderWithBearerAndContentType(token, "application/json");

  return http.put(endpoint, JSON.stringify(presentationTexts), params);
}

//Api call to Storage:Instances to update the data values for an instance by id and return response
export function putUpdateDataValues(token, instanceId, dataValues) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "datavalues");
  var params = apiHelper.buildHeaderWithBearerAndContentType(token, "application/json");

  return http.put(endpoint, JSON.stringify(dataValues), params);
}


//Function to build input json for creation of instance with app, instanceOwner details and returns a JSON object
function buildInstanceInputJson(instanceJson, appId, partyId) {
  instanceJson = JSON.parse(instanceJson);
  instanceJson.instanceOwner.partyId = partyId;
  instanceJson.appId = appId;
  return instanceJson;
}
