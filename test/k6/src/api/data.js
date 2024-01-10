import http from "k6/http";
import * as config from "../config.js";
import * as apiHelper from "../apiHelpers.js";

//Api call to Platform:Storage to upload a data to an instance and returns the response
export function postData(
  token,
  instanceId,
  attachmentContent,
  attachmentType,
  queryParams
) {
  var endpoint =
    config.buildInstanceUrl(instanceId, "", "instanceid") + "/data";

  endpoint +=
    queryParams != null
      ? apiHelper.buildQueryParametersForEndpoint(queryParams)
      : "";

  var isBinaryAttachment = typeof data === "object" ? true : false;
  var params = apiHelper.buildHeadersForData(
    isBinaryAttachment,
    attachmentType,
    token
  );
  return http.post(endpoint, attachmentContent, params);
}

//Api call to Platform:Storage to upload a data to an instance and returns the response
export function putData(
  token,
  instanceId,
  dataId,
  attachmentType,
  attachmentContent
) {
  var endpoint = config.buildInstanceUrl(instanceId, dataId, "dataid");
  var isBinaryAttachment = typeof data === "object" ? true : false;
  var params = apiHelper.buildHeadersForData(
    isBinaryAttachment,
    attachmentType,
    token
  );
  return http.put(endpoint, attachmentContent, params);
}

//Api call to Platform:Storage following a platform self link and returns the response
export function getDataFromSelfLink(token, platformSelfLink){
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.get(platformSelfLink, params);
}

//Api call to Platform:Storage to lock a data element and returns the response
export function lockData(token, instanceId, dataId) {
  var endpoint =
    config.buildInstanceUrl(instanceId, dataId, "dataid") + "/lock";
  var params = apiHelper.buildHeaderWithBearer(token);

  return http.put(endpoint, null, params);
}

//Api call to Platform:Storage to unlock a data element and returns the response
export function unlockData(token, instanceId, dataId) {
  var endpoint =
    config.buildInstanceUrl(instanceId, dataId, "dataid") + "/lock";
  var params = apiHelper.buildHeaderWithBearer(token);

  return http.del(endpoint, null, params);
}
