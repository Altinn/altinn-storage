import { check } from "k6";
import * as setupToken from "./setup-token.js";
import * as instancesApi from "./api/instances.js";

export function hardDeleteInstance(token, instanceId) {
  var res = instancesApi.deleteInstanceById(token, instanceId, true);
  check(res, {
    "// Cleanup // Delete instance. Status is 200": (r) => r.status === 200,
  });
}
