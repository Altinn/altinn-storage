/**
 * Build a string in a format of query param to the endpoint
 * @param {*} queryparams a json object with key as query name and value as query value
 * @example {"key1": "value1", "key2": "value2"}
 * @returns string a string like key1=value&key2=value2
 */

const subscriptionKey = __ENV.apimSubsKey;

export function buildQueryParametersForEndpoint(queryparams) {
  var query = "?";
  Object.keys(queryparams).forEach(function (key) {
    if (Array.isArray(queryparams[key])) {
      queryparams[key].forEach((value) => {
        query += key + "=" + value + "&";
      });
    } else {
      query += key + "=" + queryparams[key] + "&";
    }
  });
  query = query.slice(0, -1);
  return query;
}

export function buildHeaderWithBearer(token, options = {}) {
  var headers = {
    Authorization: "Bearer " + token,
    "Ocp-Apim-Subscription-Key": subscriptionKey,
  };

  if (options.instanceOwnerIdentifier) {
    headers["X-Ai-InstanceOwnerIdentifier"] = options.instanceOwnerIdentifier;
  }

  return { headers };
}

export function buildHeaderWithContentType(contentType) {
  var params = {
    headers: {
      "Content-Type": contentType,
    },
  };

  return params;
}

export function buildHeaderWithBearerAndSubscriptionKey(token, apimSubsKey) {
  var params = {
    headers: {
      Authorization: "Bearer " + token,
      "Ocp-Apim-Subscription-Key": apimSubsKey,
      "Content-Type": "application/json",
    },
  };

  return params;
}

export function buildHeaderWithBasic(token) {
  var params = {
    headers: {
      Authorization: "Basic " + token,
    },
  };

  return params;
}

export function buildHeaderWithBearerAndContentType(token, contentType) {
  var params = {
    headers: {
      Authorization: "Bearer " + token,
      "Content-Type": contentType,
      "Ocp-Apim-Subscription-Key": subscriptionKey,
    },
  };

  return params;
}

export function buildHeaderWithCookie(name, value) {
  var params = {
    headers: {
      Cookie: name + "=" + value,
    }
  };

  return params;
}


//Function to determine the headers for a POST/PUT data based on dataType
export function buildHeadersForData(isBinaryAttachment, fileType, token) {
  var params = {};
  if (isBinaryAttachment) {
    params = {
      headers: {
        Authorization: "Bearer " + token,
        "Content-Type": `${findContentType(fileType)}`,
        "Content-Disposition": `attachment; filename=test.${fileType}`,
        "Ocp-Apim-Subscription-Key": subscriptionKey,
      },
    };
  } else {
    params = {
      headers: {
        Authorization: "Bearer " + token,
        "Content-Type": "application/xml",
        "Ocp-Apim-Subscription-Key": subscriptionKey,
      },
    };
  }
  return params;
}

/**
 * Find the content type for a given type
 * @param {string} type xml, pdf, txt
 * @returns content type
 */
function findContentType(type) {
  var contentType;
  switch (type) {
    case "xml":
      contentType = "text/xml";
      break;
    case "pdf":
      contentType = "application/pdf";
      break;
    case "txt":
      contentType = "text/plain";
      break;
    default:
      contentType = "application/octet-stream";
      break;
  }
  return contentType;
}
