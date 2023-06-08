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