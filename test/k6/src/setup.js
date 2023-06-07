import http from 'k6/http';
import { check } from 'k6';
import * as config from './config.js';
import { addErrorCount, stopIterationOnFail } from './errorhandler.js';
import { b64decode } from "k6/encoding";

const environment = __ENV.env.toLowerCase();
const tokenGeneratorUserName = __ENV.tokengenuser;
const tokenGeneratorUserPwd = __ENV.tokengenuserpwd;
const scopes = __ENV.scopes;
const maskinPortenToken = __ENV.maskinporten;

//Request to Authenticate an user with Altinn userName and password and returns ASPXAUTH Cookie
export function authenticateUser(userName, userPassword) {
  var endpoint = environment != 'yt01' ? config.authentication['authenticationWithPassword'] : config.authentication['authenticationYt01'];
  var requestBody = {
    UserName: userName,
    UserPassword: userPassword,
  };
  var params = {
    headers: {
      Accept: 'application/hal+json',
    },
  };
  var res = http.post(endpoint, requestBody, params);

  var success = check(res, {
    '// Setup // Authentication towards Altinn 2 Success': (r) => r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail('// Setup // Authentication towards Altinn 2 Failed', success, res);

  const cookieName = '.ASPXAUTH';
  var cookieValue = res.cookies[cookieName][0].value;
  return cookieValue;
}

//Request to Authenticate an user and returns AltinnStudioRuntime Token
export function getAltinnStudioRuntimeToken(aspxauthCookie) {
  clearCookies();
  var endpoint = config.platformAuthentication['authentication'] + '?goto=' + config.platformAuthentication['refresh'];
  var params = {
    cookies: { '.ASPXAUTH': aspxauthCookie },
  };

  var res = http.get(endpoint, params);
  var success = check(res, {
    '// Setup // Authentication towards Altinn 3 Success': (r) => r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail('// Setup // Authentication towards Altinn 3  Failed', success, res);
  return res.body;
}

export function getTokenClaims(jwtToken){
    const parts = jwtToken.split('.');
    var claims= JSON.parse(b64decode(parts[1].toString(), "rawstd", 's'));

    return {
      userId: claims["urn:altinn:userid"],
      partyId: claims["urn:altinn:partyid"]
    }
}

//Request to get user data and returns partyId, ssn, userId, orgNr
export function getUserData(altinnStudioRuntimeCookie, appOwner, appName, orgNo) {
  clearCookies();
  var endpoint = config.appApiBaseUrl(appOwner, appName) + config.appProfile['user'];
  var params = headers.buildHeaderWithRuntime(altinnStudioRuntimeCookie, 'app');
  var res = http.get(endpoint, params);
  var success = check(res, {
    'Get User data': (r) => r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail('Get User data failed', success, res);
  res = JSON.parse(res.body);

  var userData = {
    userId: res.userId,
    ssn: res.party.ssn,
    partyId: res.partyId,
    orgNumber: orgNo,
    orgNumberPartyId: null
  };

  //get parties and find an Org that an user can represent
  res = getParties(altinnStudioRuntimeCookie, userData['userId']);
  success = check(res, {
    'Get User data': (r) => r.status === 200,
  });
  addErrorCount(success);
  stopIterationOnFail('Get User data failed', success, res);

  res = JSON.parse(res.body);
  for (var i = 0; i < res.length; i++) {
    if ( orgNo == null && res[i].orgNumber != null) {
        userData.orgNumberPartyId = res[i].partyId;
        userData.orgNumber = res[i].orgNumber;
        break;
      }
      else if( orgNo != null && orgNo == res[i].orgNumber){
        userData.orgNumberPartyId = res[i].partyId;
        break;
      }
    }
  return userData;
}

//Function to clear the cookies under baseurl by setting the expires field to a past date
export function clearCookies() {
  var jar = http.cookieJar();
  jar.set('https://' + config.baseUrl, 'AltinnStudioRuntime', 'test', { expires: 'Mon, 02 Jan 2010 15:04:05 MST' });
  jar.set('https://' + config.baseUrl, '.ASPXAUTH', 'test', { expires: 'Mon, 02 Jan 2010 15:04:05 MST' });
}

/**
 * generate an altinn token for TTD based on the environment
 * use exchange token if prod, and altinnTestTools for test environments
 * @returns altinn token with the scopes for an org/appowner
 */
export function getAltinnTokenForTTD() {
  if (environment === 'prod') {
    return convertMaskinPortenToken(maskinPortenToken, 'true');
  } else {
    var queryParams = {
      env: environment,
      scopes: scopes,
      org: 'ttd',
      orgNo: '991825827',
    };
    return generateToken('enterprise', tokenGeneratorUserName, tokenGeneratorUserPwd, queryParams);
  }
}

export function getSBLBuildVersion() {
  var endpoint = config.sbl['altinnBuildVersion'];
  var res = http.get(endpoint);
  return res.body.replace(/[^0-9a-zA-Z.]/g, "");
}
