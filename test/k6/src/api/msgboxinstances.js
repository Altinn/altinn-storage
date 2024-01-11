import http from "k6/http";
import * as apiHelper from "../apiHelpers.js";
import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";

const sblAccesKey = __ENV.apimSblSubsKey;

export function getInstanceById(token, instanceId) {
  validateEnvironmentVars();

  var endpoint = config.buildMessageboxInstanceUrl(instanceId, "instanceid");
  var params = apiHelper.buildHeaderWithBearerAndSubscriptionKey(
    token,
    sblAccesKey
  );
  return http.get(endpoint, params);
}

export function searchInstances(token, queryModel) {
  validateEnvironmentVars();

  var endpoint = config.buildMessageboxInstanceUrl("", "search");
  var params = apiHelper.buildHeaderWithBearerAndSubscriptionKey(
    token,
    sblAccesKey
  );
  var requestBody = JSON.stringify(queryModel);

  return http.post(endpoint, requestBody, params);
}

export function getInstanceEvents(token, instanceId) {
  validateEnvironmentVars();

  var endpoint = config.buildMessageboxInstanceUrl(instanceId, "events");
  var params = apiHelper.buildHeaderWithBearerAndSubscriptionKey(
    token,
    sblAccesKey
  );
  return http.get(endpoint, params);
}

export function deleteInstance(token, instanceId, hardDelete) {
  validateEnvironmentVars();

  var endpoint =
    config.buildMessageboxInstanceUrl(instanceId, "instanceid") +
    "?hard=" +
    hardDelete;

  var params = apiHelper.buildHeaderWithBearerAndSubscriptionKey(
    token,
    sblAccesKey
  );
  return http.del(endpoint, null, params);
}

export function undeleteInstance(token, instanceId) {
  validateEnvironmentVars();

  var endpoint = config.buildMessageboxInstanceUrl(instanceId, "undelete");

  var params = apiHelper.buildHeaderWithBearerAndSubscriptionKey(
    token,
    sblAccesKey
  );
  return http.put(endpoint, null, params);
}

function validateEnvironmentVars() {
  if (!sblAccesKey) {
    stopIterationOnFail(
      "Required environment variable APIM subscription key with SBL access(apimSblSubsKey) was not provided",
      false
    );
  }
}
