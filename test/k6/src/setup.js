import http from "k6/http";
import { check } from "k6";
import * as config from "./config.js";
import { addErrorCount, stopIterationOnFail } from "./errorhandler.js";
import { b64decode } from "k6/encoding";
import * as tokenGenerator from "./api/token-generator.js";


const useTestTokenGenerator = __ENV.useTestTokenGenerator
? __ENV.useTestTokenGenerator.toLowerCase().includes("true")
: false;
const environment = __ENV.env.toLowerCase();

//Request to Authenticate an user with Altinn userName and password and returns ASPXAUTH Cookie
export function authenticateUser(userName, userPassword) {
  var endpoint =
    environment != "yt01"
      ? config.authentication["authenticationWithPassword"]
      : config.authentication["authenticationYt01"];
  var requestBody = {
    UserName: userName,
    UserPassword: userPassword,
  };
  var params = {
    headers: {
      Accept: "application/hal+json",
    },
  };
  var res = http.post(endpoint, requestBody, params);

  var success = check(res, {
    "// Setup // Authentication towards Altinn 2 Success": (r) =>
      r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Authentication towards Altinn 2 Failed",
    success,
    res
  );

  const cookieName = ".ASPXAUTH";
  var cookieValue = res.cookies[cookieName][0].value;
  return cookieValue;
}

//Request to Authenticate an user and returns AltinnStudioRuntime Token
export function getAltinnStudioRuntimeToken(aspxauthCookie) {
  clearCookies();
  var endpoint =
    config.platformAuthentication["authentication"] +
    "?goto=" +
    config.platformAuthentication["refresh"];
  var params = {
    cookies: { ".ASPXAUTH": aspxauthCookie },
  };

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

export function getTokenClaims(jwtToken) {
  const parts = jwtToken.split(".");
  var claims = JSON.parse(b64decode(parts[1].toString(), "rawstd", "s"));

  return {
    userId: claims["urn:altinn:userid"],
    partyId: claims["urn:altinn:partyid"],
  };
}

//Function to clear the cookies under baseurl by setting the expires field to a past date
export function clearCookies() {
  var jar = http.cookieJar();
  jar.set("https://" + config.baseUrl, "AltinnStudioRuntime", "test", {
    expires: "Mon, 02 Jan 2010 15:04:05 MST",
  });
  jar.set("https://" + config.baseUrl, ".ASPXAUTH", "test", {
    expires: "Mon, 02 Jan 2010 15:04:05 MST",
  });
}

export function getAltinnTokenForUser(
  userId,
  partyId,
  pid,
  username,
  password
) {
  if (!useTestTokenGenerator) {
    var aspxauthCookie = authenticateUser(username, password);
    return  getAltinnStudioRuntimeToken(aspxauthCookie);
  } else {
    var queryParams = {
      env: environment,
      userId: userId,
      partyId: partyId,
      pid: pid,
    };

    return tokenGenerator.generatePersonalToken(queryParams);
  }
}
