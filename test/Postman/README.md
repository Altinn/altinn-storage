# Postman request collection for Storage

This Postman collection contains request examples using the Altinn Storage API. This is not a guide for how to use Postman.

## Environments

The primary QA environment for external parties are the TT02 environment. Internally there are also 4 acceptance test environments that are rotated to represent different versions of Altinn. Use the values below as environment variables for use by the Postman collection.

| Environment | BasePath                           | Port number     |
|-------------|------------------------------------|-----------------|
| Development | http://localhost                   | :5010           |
| AT21        | https://platform.at21.altinn.cloud | :433 (or blank) |
| AT22        | https://platform.at22.altinn.cloud | :433 (or blank) |
| AT23        | https://platform.at23.altinn.cloud | :433 (or blank) |
| AT24        | https://platform.at24.altinn.cloud | :433 (or blank) |
| TT02        | https://platform.tt02.altinn.no    | :443 (or blank) |
| Production  | https://platform.altinn.no         | :433 (or blank) |

## Authentication & Authorization

Most of the API endpoint will require som sort of authentication using a bearer token signed by Altinn Authentication. The Authentication system in Altinn function as an OpenID-Connect provider for the other systems in Altinn 3. https://docs.altinn.studio/api/authentication/. 

When it comes to simple testing there is also an online token generator that can be used. https://github.com/Altinn/AltinnTestTools. 

### Scopes

Some endpoints require scopes. The bearer token needs to contain claims with the necessary scopes.

| Name | Description |
|-------------|-----------------------------------------------------|
| altinn:serviceowner/instances.read | An application owner is required to have this scope in order to list instances.
| altinn:serviceowner/instances.write | An application owner is required to have this scope perform mutating operations. This scope is effectively useless as there are no endpoints that allow mutation. Use the App API instead. 

There are currently no scopes required for end user systems.

## Request organisation

Requests are loosely organized in folders with requests against different parts of the Storage API.

### Applications

This folder holds a few requests for retrieving metadata about apps deployed in the selected environment. These endpoints do not require any for of authentication or authorization, they are publicly available.

### Instances & data

This folder holds requests for retrieving instances and data blobs. These endpoints requires an authenticated user using a bearer token. The bearer token must have been signed by Authentication in Altinn.

#### Postman Variables

This is a list of the Postman variables used in the requests.

| Name | Description |
|-------------|-----------------------------------------------------|
| Org | The short acronym identifier of an application owner.                    |
| AppName | The identifying name of an app. App ID is the combination of Org and AppName separated by a '/' and it's found in the app URL in all environments. |
| InstanceId | Unique identifier for a specific instance. The value contains two distinct parts separated by a '/'. The first value is the instance owner party id, which is an integer. The other value is a generated UUID. |
