import http from "k6/http";
import * as apiHelper from "../apiHelpers.js";
import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";

export function postText(token, org, app, serializedTextResource) {
  var endpoint = config.buildAppUrl(org, app, "texts");

  var params = apiHelper.buildHeaderWithBearerAndContentType(
    token,
    "application/json"
  );

  var requestBody = serializedTextResource;
  return http.post(endpoint, requestBody, params);
}

export function getTexts(token, org, app, language) {
  var endpoint = config.buildAppUrl(org, app, "texts") + "/" + language;
  var params = apiHelper.buildHeaderWithBearer(token);
  return http.get(endpoint, params);
}
