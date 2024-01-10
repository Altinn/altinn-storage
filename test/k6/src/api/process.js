import http from "k6/http";
import * as apiHelper from "../apiHelpers.js";
import * as config from "../config.js";

export function putProcess(token, instanceId, serializedProcessState) {
  var endpoint = config.buildInstanceUrl(instanceId, null, "process");
  var params = apiHelper.buildHeaderWithBearerAndContentType(
    token,
    "application/json"
  );

  return http.put(endpoint, serializedProcessState, params);
}

export function getProcessHistory(token, instanceId) {
  var endpoint =
    config.buildInstanceUrl(instanceId, null, "process") + "/history";
  var params = apiHelper.buildHeaderWithBearer(token);

  return http.get(endpoint, params);
}
