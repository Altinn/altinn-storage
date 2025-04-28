import { check } from "k6";
import http from "k6/http";

import {
  buildHeaderWithBearer,
  buildHeaderWithContentType,
  buildHeaderWithCookie,
} from "../apiHelpers.js";
import { platformAuthentication, portalAuthentication, authCookieName } from "../config.js";
import { stopIterationOnFail, addErrorCount } from "../errorhandler.js";

const userName = __ENV.userName;
const userPassword = __ENV.userPassword;
const aspxAuthCookieName = authCookieName;

export function exchangeToAltinnToken(token, test) {
  var endpoint = platformAuthentication.exchange + "?test=" + test;
  var params = buildHeaderWithBearer(token);

  var res = http.get(endpoint, params);
  var success = check(res, {
    "// Setup // Authentication towards Altinn 3 Success": (r) =>
      r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Authentication towards Altinn 3  Failed",
    success,
    res
  );

  return res.body;
}

export function authenticateUser() {
  if (!userName) {
    stopIterationOnFail(
      "Required environment variable username (userName) was not provided",
      false
    );
  }

  if (!userPassword) {
    stopIterationOnFail(
      "Required environment variable user password (userPassword) was not provided",
      false
    );
  }

  var aspxAuthCookie = getAspxAuth(userName, userPassword);
  var runtimeToken = getRuntimeTokenFromCookie(aspxAuthCookie);
  return runtimeToken;
}

function getRuntimeTokenFromCookie(cookie) {
  var endpoint = platformAuthentication.refresh;

  var params = buildHeaderWithCookie(aspxAuthCookieName, cookie);
  var res = http.get(endpoint, params);
  var success = check(res, {
    "// Setup // Authentication towards Altinn 3 Success": (r) =>
      r.status === 200,
  });

  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Authentication towards Altinn 3 Success",
    success,
    res
  );

  return res.body;
}

function getAspxAuth(userName, userPassword) {
  var endpoint = portalAuthentication.authenticateWithPwd;

  var requestBody = {
    UserName: userName,
    UserPassword: userPassword,
  };

  var params = buildHeaderWithContentType("application/json");

  var res = http.post(endpoint, JSON.stringify(requestBody), params);

  var success = check(res, {
    "// Setup // Authentication towards Altinn 2 Success": (r) =>
      r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Authentication towards Altinn 2 Success",
    success,
    res
  );

  var aspxAuthCookie = res.cookies[aspxAuthCookieName][0].value;

  return aspxAuthCookie;
}
