## k6 test project for automated tests

# Getting started


Install pre-requisites
## Install k6

*We recommend running the tests through a docker container.*

From the command line:

> docker pull grafana/k6


Further information on [installing k6 for running in docker is available here.](https://k6.io/docs/get-started/installation/#docker)


Alternatively, it is possible to run the tests directly on your machine as well.

[General installation instructions are available here.](https://k6.io/docs/get-started/installation/)


## Running tests

All tests are defined in `src/tests` and in the top of each test file an example of the cmd to run the test is available.

The command should be run from the root of the k6 folder.

>$> cd /altinn-storage/test/k6

Run test suite by specifying filename.

For example:

  >$> podman compose run k6 run /src/tests/data.js  -e env=***  -e userId=*** -e partyId=*** -e pid=*** -e org=ttd -e app=*** -e apimSubsKey=*** -e tokenGeneratorUserName=*** -e tokenGeneratorUserPwd=*** -e runFullTestSet=true -e useTestTokenGenerator=true


The command consists of three sections

`podman compose run` to run the test in a docker container

`k6 run {path to test file}` pointing to the test file you want to run e.g. /src/tests/data.js

`-e env=*** -e org=ttd -e app=*** -e apimSubsKey=*** -e userId=*** -e partyId=*** -e pid=*** -e useTestTokenGenerator=true -e tokenGeneratorUserName=*** -e tokenGeneratorUserPwd=*** -e runFullTestSet=true`  all environment variables that should be included in the request.



## Variables
- `env` the Altinn environmen to run tests toards; AT2x, TT02, YT01, prod
- `org` the app owner of the application used for testing
- `app` the name of the application used for testing
- `apimSubsKey` subscription key for a subscription of the product `AppsAccess` (for signing and data)
- `userId`\* the user id for the test user
- `pid`\* the person number (SSN) for the test user
- `partyId`\* the party id for the test user
- `tokenGeneratorUserName`\* username for Altinn Test Tools token generator (Check AltinnPedia for details)
- `tokenGeneratorUserPwd`\* password for Altinn Test Tools token generator (Check AltinnPedia for details)
- `useTestTokenGenerator` \* variable to enable use of test tools, set to true if required
- `username` username of test user
- `userpwd` password for test user

\* required when using Altinn Test Tools login, available for all environments except prod
\** required for username/password authentication via Altinn 2, available for all environments, but AI-DEV network is required for AT and YT

