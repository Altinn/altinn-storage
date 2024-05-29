import http from "k6/http";
import * as apiHelper from "../apiHelpers.js";
import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";

/**
 * Api call to Storage:Instances to create an app instance and returns response
 * @param {Object} instantiationOptions Object containing the parameters for the API call.
 * @param {string} instantiationOptions.token Token value to be sent in apiHelper for authentication.
 * @param {string} instantiationOptions.partyId Party ID of the user to whom the instance is to be created.
 * @param {string} instantiationOptions.org Short name of the app owner.
 * @param {string} instantiationOptions.app App name.
 * @param {JSON} instantiationOptions.serializedInstance Instance JSON metadata sent in the request body.
 * @param {string} [instantiationOptions.personNumber] Optional person number to be included in the instance owner.
 * @param {string} [instantiationOptions.orgNumber] Optional organization number to be included in the instance owner.
 * @returns {JSON} JSON object including response status, body, and timings.
 */
export function postInstance(instantiationOptions = {}) {
  const appId = `${instantiationOptions.org}/${instantiationOptions.app}`;
  const endpoint = `${config.platformStorage.instances}?appId=${appId}`;

  const params = apiHelper.buildHeaderWithBearerAndContentType(
    instantiationOptions.token,
    "application/json"
  );

  const instanceJson = JSON.parse(instantiationOptions.serializedInstance);
  instanceJson.instanceOwner.partyId = instantiationOptions.partyId;
  instanceJson.appId = appId;

  if (instantiationOptions.personNumber) {
    instanceJson.instanceOwner.personNumber = instantiationOptions.personNumber;
  } else if (instantiationOptions.orgNumber) {
    instanceJson.instanceOwner.organisationNumber = instantiationOptions.orgNumber;
  }

  const requestBody = JSON.stringify(instanceJson);

  return http.post(endpoint, requestBody, params);
}


//Api call to Storage:Instances to get an instance by id and return response
export function getInstanceById(token, instanceId) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "instanceid");
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.get(endpoint, params);
}
//Api call to Storage:Instances to get instances based on filter parameters and return response
export function getInstances(token, filters, options = {}) {
  var endpoint = config.platformStorage["instances"];
  endpoint += apiHelper.buildQueryParametersForEndpoint(filters);
  var params = apiHelper.buildHeaderWithBearer(token, options);
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

  return http.post(endpoint, "", params);
}

//Api call to Storage:Instances to update the read status to: Unread, Read, UpdatedSinceLastReview
//an instance by id and return response
export function putReadStatus(token, instanceId, readStatus) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "readstatus") + "?status=" + readStatus;
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.put(endpoint, null, params);
}

//Api call to Storage:Instances to update the presentation texts for an instance by id and return response
export function putPresentationTexts(token, instanceId, presentationTexts) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "presentationtexts");
  var params = apiHelper.buildHeaderWithBearerAndContentType(token, "application/json");

  return http.put(endpoint, JSON.stringify(presentationTexts), params);
}

//Api call to Storage:Instances to update the data values for an instance by id and return response
export function putDataValues(token, instanceId, dataValues) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "datavalues");
  var params = apiHelper.buildHeaderWithBearerAndContentType(token, "application/json");

  return http.put(endpoint, JSON.stringify(dataValues), params);
}

//Api call to Storage:Instances to set substatus on an instance by id and return response
export function putSubstatus(token, instanceId, substatus) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "substatus");
  var params = apiHelper.buildHeaderWithBearerAndContentType(token, "application/json");

  return http.put(endpoint, JSON.stringify(substatus), params);
}