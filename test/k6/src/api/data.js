import http from "k6/http";
import * as config from "../config.js";
import * as apiHelper from "../apiHelpers.js";

//Api call to Storage:Data to upload a data to an instance and returns the response
export function postData(
  token,
  instanceId,
  fileContent,
  fileType,
  dataType,
  tags = null
) {
  // ensuring dataType is included in query params amongst tags
  tags = tags || {};
  tags.dataType = dataType;

  var endpoint = config.buildInstanceUrl(instanceId, "", "data");
  endpoint += apiHelper.buildQueryParametersForEndpoint(tags);

  var isBinaryAttachment = typeof data === "object" ? true : false;
  var params = apiHelper.buildHeadersForData(
    isBinaryAttachment,
    fileType,
    token
  );
  return http.post(endpoint, fileContent, params);
}

//Api call to Storage:Data retrieves a dataElement by id and returns the response
export function getDataById(token, instanceId, dataElementId) {
  var endpoint = config.buildInstanceUrl(instanceId, dataElementId, "data");
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.get(endpoint, params);
}
//Api call to Storage:Data following a platform self link and returns the response
export function getDataFromSelfLink(token, platformSelfLink) {
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.get(platformSelfLink, params);
}

//Api call to Storage:Data to get all data elements and returns the response
export function getAllDataElements(token, instanceId) {
  var endpoint = config.buildInstanceUrl(instanceId, "", "dataelements");
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.get(endpoint, params);
}

//Api call to Storage:Data to upload a data to an instance and returns the response
export function putData(
  token,
  instanceId,
  dataId,
  fileType,
  fileContent
) {
  var endpoint = config.buildInstanceUrl(instanceId, dataId, "data");
  var isBinaryAttachment = typeof data === "object" ? true : false;
  var params = apiHelper.buildHeadersForData(
    isBinaryAttachment,
    fileType,
    token
  );
  return http.put(endpoint, fileContent, params);
}

//Api call to Storage:Data to lock a data element and returns the response
export function lockData(token, instanceId, dataId) {
  var endpoint = config.buildInstanceUrl(instanceId, dataId, "data") + "/lock";
  var params = apiHelper.buildHeaderWithBearer(token);

  return http.put(endpoint, null, params);
}

//Api call to Storage:Data to unlock a data element and returns the response
export function unlockData(token, instanceId, dataId) {
  var endpoint = config.buildInstanceUrl(instanceId, dataId, "data") + "/lock";
  var params = apiHelper.buildHeaderWithBearer(token);

  return http.del(endpoint, null, params);
}

//Api call to Platform:Storage to delete a data by id from an instance and returns the response
export function deleteData(token, instanceId, datalementId) {
  var endpoint = config.buildInstanceUrl(instanceId, datalementId, "data")
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.del(endpoint, null, params);
}
