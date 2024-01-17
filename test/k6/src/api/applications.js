import http from "k6/http";
import * as apiHelper from "../apiHelpers.js";
import * as config from "../config.js";
import { stopIterationOnFail } from "../errorhandler.js";

export function getAppsForOrg(org) {
  var endpoint = config.buildAppUrl(org, "", "application");

  return http.get(endpoint);
}

export function getApp(org, app) {
  var endpoint = config.buildAppUrl(org, app, "application");

  return http.get(endpoint);
}
