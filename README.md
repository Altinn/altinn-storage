# Altinn Storage

## Build status
[![Storage build status](https://dev.azure.com/brreg/altinn-studio/_apis/build/status/altinn-platform/storage-master?label=platform/storage)](https://dev.azure.com/brreg/altinn-studio/_build/latest?definitionId=30)


## Getting Started

These instructions will get you a copy of the storage component up and running on your machine for development and testing purposes.

### Prerequisites

* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Newest [Git](https://git-scm.com/downloads)
* A code editor - we like [Visual Studio Code](https://code.visualstudio.com/download)
   - Also install [recommended extensions](https://code.visualstudio.com/docs/editor/extension-marketplace#_workspace-recommended-extensions) (e.g. [C#](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csharp))
* Infrastructure
  * [Podman](https://podman.io/) or another container tool such as Docker Desktop
  * [PostgreSQL](https://www.postgresql.org/download/)
  * [pgAdmin](https://www.pgadmin.org/download/)
  * Automatically setup using `docker compose up -d`

### Manually setting up PostgreSQL

Ensure that both PostgreSQL and pgAdmin have been installed and start pgAdmin.
If you installed prerequisite infrastructure through `docker compose`, you can skip to the next section.

In pgAdmin
- Create database _storagedb_
- Create the following users with password: _Password_ (see privileges in parentheses)
  - platform_storage_admin (superuser, canlogin)
  - platform_storage (canlogin)
- Create schema _storage_ in storagedb with owner _platform_storage_admin_

A more detailed description of the database setup is available in [our developer handbook](https://docs.altinn.studio/community/contributing/handbook/postgres/)

### Cloning the application

Clone [Altinn Storage repo](https://github.com/Altinn/altinn-storage) and navigate to the folder.

```bash
git clone https://github.com/Altinn/altinn-storage
cd altinn-storage
```

### Run tests

You can run the tests by executing

```bash
dotnet test Altinn.Platform.Storage.sln
```

### Running the application in a docker container

- Start Altinn Storage docker container run the command

  ```cmd
  podman compose up -d --build
  ```

- To stop the container running Altinn Storage run the command

  ```cmd
  podman stop altinn-storage
  ```

### Running the application with .NET

The Storage components can be run locally when developing/debugging. Follow the install steps above if this has not already been done.

- Navigate to _src/Storage_, and build and run the code from there, or run the solution using you selected code editor

  ```cmd
  cd src/Storage
  dotnet run
  ```

The storage solution is now available locally at http://localhost:5010/.
To access swagger use http://localhost:5010/swagger.
