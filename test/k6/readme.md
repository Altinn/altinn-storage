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

    docker-compose run k6 run /src/tests/data.js `
    -e env=*** `
    -e username=*** `
    -e userpwd=*** `
    -e org=ttd `
    -e app=*** `
    -e apimSubsKey=*** `
    -e runFullTestSet=true

[TODO] include example