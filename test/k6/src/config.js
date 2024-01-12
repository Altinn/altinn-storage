// This file contains baseURLs and endpoints for the APIs
export var baseUrls = {
  at21: "at21.altinn.cloud",
  at22: "at22.altinn.cloud",
  at23: "at23.altinn.cloud",
  at24: "at24.altinn.cloud",
  tt02: "tt02.altinn.no",
  yt01: "yt01.altinn.cloud",
  prod: "altinn.no",
};

//Get values from environment
const environment = __ENV.env.toLowerCase();
export let baseUrl = baseUrls[environment];

//Altinn API
export var authentication = {
  authenticationWithPassword:
    "https://" + baseUrl + "/api/authentication/authenticatewithpassword",
  authenticationYt01:
    "https://yt01.ai.basefarm.net/api/authentication/authenticatewithpassword",
};

//AltinnTestTools
export var tokenGenerator = {
  getEnterpriseToken:
    "https://altinn-testtools-token-generator.azurewebsites.net/api/GetEnterpriseToken",
  getPersonalToken:
    "https://altinn-testtools-token-generator.azurewebsites.net/api/GetPersonalToken",
};

//Platform APIs
//Authentication
export var platformAuthentication = {
  authentication:
    "https://platform." + baseUrl + "/authentication/api/v1/authentication",
  refresh: "https://platform." + baseUrl + "/authentication/api/v1/refresh",
};

//Platform Storage
export var platformStorage = {
  applications: "https://platform." + baseUrl + "/storage/api/v1/applications",
  instances: "https://platform." + baseUrl + "/storage/api/v1/instances",
  sblInstances: "https://platform." + baseUrl + "/storage/api/v1/sbl/instances",
};

//Function to build endpoints in storage related to an application with org and app
//and returns the endpoint
export function buildAppUrl(org, app, type) {
  var value = "";
  switch (type) {
    case "application":
      value = platformStorage["applications"] + "/" + org + "/" + app;
      break;
    case "texts":
      value = value =
        platformStorage["applications"] + "/" + org + "/" + app + "/texts";
      break;
  }
  return value;
}

//Function to build endpoints in storage related to an instance with instanceOwnerId, instanceId, dataId, type
//and returns the endpoint
export function buildInstanceUrl(instanceId, dataId, type) {
  var value = "";
  switch (type) {
    case "instanceid":
      value = platformStorage["instances"] + "/" + instanceId;
      break;
    case "data":
      value =
        platformStorage["instances"] + "/" + instanceId + "/data/" + dataId;
      break;
    case "dataelements":
    case "process":
    case "sign":
    case "complete":
    case "readstatus":
    case "presentationtexts":
    case "datavalues":
    case "substatus":
      value = platformStorage["instances"] + "/" + instanceId + "/" + type;
      break;
  }
  return value;
}

//Function to build endpoints in storage related to messagebox instances
//and returns the endpoint
export function buildMessageboxInstanceUrl(instanceId, type) {
  var value = "";
  switch (type) {
    case "search":
      value = platformStorage["sblInstances"] + "/search";
      break;
    case "instanceid":
      value = platformStorage["sblInstances"] + "/" + instanceId;
      break;
    case "events":
      value =
        platformStorage["sblInstances"] + "/" + instanceId + "/" + "events";
      break;
    case "undelete":
      value =
        platformStorage["sblInstances"] + "/" + instanceId + "/" + "undelete";
      break;
  }
  return value;
}
