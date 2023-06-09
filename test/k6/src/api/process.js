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
export function putProcess(token, instanceId, serializedProcessState) {
  var endpoint = config.buildStorageUrls(instanceId, null, "process");
  var params = apiHelper.buildHeaderWithBearerAndContentType(
    token,
    "application/json"
  );

  return http.put(endpoint, serializedProcessState, params);
}