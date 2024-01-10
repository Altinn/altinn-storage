import { check } from "k6";
import * as instancesApi from "./api/instances.js";
import { addErrorCount, stopIterationOnFail } from "./errorhandler.js";
let serializedInstance = open("./data/instance.json");

export function getInstanceForTest(token, partyId, org, app) {
  var res = instancesApi.postInstance(
    token,
    partyId,
    org,
    app,
    serializedInstance
  );

  var success = check(res, {
    "// Setup // Generating instance for test": (r) => r.status === 201,
  });
  addErrorCount(success);
  stopIterationOnFail(
    "// Setup // Generating instance for test Failed",
    success,
    res
  );
  return JSON.parse(res.body)["id"];
}