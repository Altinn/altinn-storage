# AGENTS.md

This file provides guidance to AI agents when working with code.

Altinn Storage is the platform component that stores application **instances**, their **data elements** (blobs), **instance events**, and related application/text/organisation metadata. It is the persistence backbone for Altinn apps.

## Backend Stack

Altinn Storage's backend is built using the following technologies:

- .NET 10 (ASP.NET Core Web API)
- PostgreSQL, accessed via Npgsql calling **versioned stored functions/procedures** (no ORM)
- Yuniql for database migrations (applied on application startup)
- WolverineFx with Azure Service Bus for messaging, using an outbox pattern
- Azure Blob Storage for data element payloads (Azurite locally)
- Altinn.Common.PEP for XACML/PDP authorization decisions
- OpenTelemetry with an Azure Monitor exporter for telemetry
- Swashbuckle/Swagger for API documentation
- xUnit with Moq for unit testing, and Verify for snapshot tests
- CSharpier for code formatting (enforced in CI)
- Docker/Podman support for local infrastructure and containerization

## Project Structure

The solution file is `Altinn.Platform.Storage.slnx` (XML-based `.slnx` format — use the `dotnet` CLI or VS 17.13+).

### Core Projects

- **`src/Storage/`** - Main ASP.NET Core Web API application (`Altinn.Platform.Storage.csproj`) containing:
    - `Controllers/` - API endpoints (instances, data elements, messagebox, cleanup, etc.)
    - `Services/` - Business logic and domain services
    - `Repository/` - Persistence layer: `I*Repository` interfaces with `Pg*` PostgreSQL implementations
    - `Models/` - Data models, query parameter models, and DTOs
    - `Migration/` - Yuniql SQL migrations and the stored-function/procedure source files
    - `Authorization/` - Authorization helpers built on Altinn.Common.PEP
    - `Clients/` - HTTP clients for other platform components
    - `Telemetry/` - OpenTelemetry enrichers and metrics
    - `Helpers/`, `Configuration/`, `Health/` - Cross-cutting concerns

- **`src/Storage.Interface/`** - Shared DTOs/models (`Instance`, `DataElement`, etc.). Note: the API consumes these via the published **`Altinn.Platform.Storage.Interface` NuGet package** (pinned in the csproj), **not** a project reference — changes to `src/Storage.Interface` do not flow into the API until published. Confirm which one you need before editing.

- **`src/DbTools/`** - Build-time console tool that generates the SQL functions/procedures migration script (see Coding guidelines).

### Test Projects

- **`test/UnitTest/`** - Unit and integration tests for the Storage API, organized by area (`TestingControllers/`, `TestingRepositories/`, `TestingServices/`, `ModelTests/`, `HelperTests/`), with DB helpers in `Utils/PostgresUtil.cs`.
- **`test/Altinn.Platform.Storage.Interface.Tests/`** - Tests for the shared interface models.

## Development Commands

### Build & Run

- `dotnet build Altinn.Platform.Storage.slnx` - Build the solution (also regenerates the SQL functions script via DbTools as a post-build step)
- `dotnet run --project src/Storage` - Run the API locally at http://localhost:5010 (swagger at `/swagger`)
- `dotnet watch --project src/Storage` - Run with hot reload
- `docker compose up -d` - Start local infrastructure (Postgres on 5432, pgAdmin on 8888, Azurite). Usually not needed if a local Postgres instance is already running.

### Testing

- `dotnet test Altinn.Platform.Storage.slnx` - Run all tests
- `dotnet test test/UnitTest/Altinn.Platform.Storage.UnitTest.csproj` - Run just the main test project
- `dotnet test test/UnitTest/Altinn.Platform.Storage.UnitTest.csproj --filter "FullyQualifiedName~TestingControllers.InstancesControllerTests"` - Run specific tests

## Coding guidelines

- **CSharpier** is the enforced formatter; CI runs `dotnet csharpier check .`. Run `dotnet tool restore` once, then `dotnet csharpier format .` before committing (the MSBuild integration also formats on build).
- On CI (`CI=true`) **warnings are treated as errors**. StyleCop (`SA*`) rule severities are configured individually in `.editorconfig` (some `error`, most `warning`, some `none`), so a rule at `warning` severity still fails CI even though it builds locally as a warning — keep code warning-clean.
- Follow the existing style: file-scoped namespaces and expression-bodied properties/accessors/lambdas (but not methods/constructors). Many files begin with `#nullable disable`; match the file you are in rather than introducing nullable annotations piecemeal.
- **Changing database logic:** the data layer calls versioned functions/procedures by name (e.g. `storage.readinstancefromquery_v7`). Edit the source `.sql` files in `Migration/FunctionsAndProcedures/` (bump the version suffix, e.g. `_v7` → `_v8`, when changing a signature or behavior that must not break running instances) and update the calling `Pg*Repository`. **Never edit the generated `Migration/vX.YZ/02-functions-and-procedures.sql`** — DbTools regenerates it on build. Schema/structural changes go in a new `Migration/vX.YZ/` folder and are applied by Yuniql on startup.
- **Comments describe the code, not the change.** A comment must make sense to someone reading the file a year from now with no knowledge of the task that produced it. Never write comments that narrate a fix or a diff — no "fixes the deadlock we saw in …", "previously this used …", "added to handle the bug where …", "changed because the test failed". That reasoning belongs in the commit message or PR description, not the source. Prefer a short comment on *why* the code is the way it is (a non-obvious invariant, a constraint from another component, a deliberate deviation) over a long one restating what the code already says. Default to no comment when the code is self-explanatory; use XML doc comments (`///`) on public members as the codebase already does.
- Keep controllers thin: validation and normalization that isn't HTTP-specific belongs on the models (see `InstanceQueryParameters`) or in services.
- Guard/short-circuit helpers in controllers follow a "return `null` to continue, non-null `ActionResult` to return immediately" convention.
- Preserve literal error-message strings and status codes (including the `499` used for cancelled requests) that tests assert on verbatim when refactoring.
- Run `git config blame.ignoreRevsFile .git-blame-ignore-revs` — bulk CSharpier/namespace commits are listed there.

## Testing Guidelines

- Most repository, controller, and service tests are **DB-backed**: they connect to `storagedb` (users `platform_storage_admin` and `platform_storage`, password `Password`) via `PostgresUtil` and fail with Npgsql connection errors if Postgres is not running (`docker compose up -d`). Controller tests using mocked repositories (e.g. the `GetInstances` query-endpoint tests) run without a database.
- Mock external dependencies using `Moq`
- Follow the Arrange, Act, Assert pattern
- Place test files in the same folder structure as the source files
- Some tests use **Verify** snapshots (`*.verified.txt`) — update the snapshot when output changes (e.g. the `AspNetCoreMetricsEnricher` actions snapshot when adding endpoints)
- Use `PostgresUtil.FreezeTime(...)` to override `now()`/`clock_timestamp()` for deterministic time-based tests
