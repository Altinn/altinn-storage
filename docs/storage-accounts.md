# Storage accounts per blob version

Storage keeps every data element blob in a storage account that belongs to the application owner.
Until now the account was derived at runtime from a naming pattern. From migration v0.39 the table
`storage.storageaccounts` can name an account explicitly, and every new blob version records which
account holds its blob. This document describes how the table is used.

## The table

```sql
CREATE TABLE storage.storageaccounts (
    id SMALLINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider TEXT NOT NULL,                 -- 'azure-blob', 'file-system'; selects the C# type
    providerinfo JSONB NOT NULL,            -- that provider's own properties
    org TEXT NOT NULL,                      -- blob storage org
    storageaccountnumber INT NOT NULL,      -- Application.StorageAccountNumber, 1 when the app has none
    writable BOOLEAN NOT NULL DEFAULT true, -- receives new blob versions of (org, number)
    created TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX storageaccounts_writable
    ON storage.storageaccounts (org, storageaccountnumber) WHERE writable;
```

A row is a location, never a credential. The `azure-blob` document names only the account and the
container; the service derives the URL from the account name and authenticates with its own
workload identity. A document with any other key is rejected.

| provider | providerinfo | How the service reaches it |
| --- | --- | --- |
| `azure-blob` | `{"accountName":"ttdaltinnprodstrg01","container":"ttd-prod-appsdata-blob-db"}` | `https://{accountName}.blob.core.windows.net/{container}` with the pod's workload identity, which needs the Storage Blob Data Contributor role on the account. The Azurite account `devstoreaccount1` uses the endpoint and shared key from `AzureStorageConfiguration`. |
| `file-system` | `{"directory":"/data/blobs/ttd"}` | A directory under `StorageAccounts:FileSystem:RootDirectory`, with the pod's file permissions. |

Rows are immutable except for `writable`. An id, once handed out, always means the same location.

```mermaid
erDiagram
    storageaccounts {
        smallint id PK
        text provider
        jsonb providerinfo
        text org
        int storageaccountnumber
        boolean writable
    }
    dataelementblobversions {
        uuid id PK
        uuid dataelementid
        text blobstorageorg
        int storageaccountnumber "NULL means 1"
        smallint storageaccountid FK "NULL means the pattern default"
        timestamptz detachedat
    }
    dataelements {
        uuid id PK
        uuid currentblobversion FK
    }
    storageaccounts ||--o{ dataelementblobversions : "holds the blob of"
    dataelements |o--o| dataelementblobversions : "current version"
```

## Two ways to name an account

Every blob version is located in one of two ways.

- **By row.** `storageaccountid` on the version row points at a `storageaccounts` row. The blob is in
  the location that row describes.
- **By pattern.** `storageaccountid` is `NULL`. The account name is built from the configured pattern
  (`AzureStorageConfiguration:OrgStorageAccount` and `OrgStorageContainer`) and the version row's own
  `blobstorageorg` and `storageaccountnumber`, exactly as before v0.39. This is called the *pattern
  default* in the code.

The pattern default covers every blob written before v0.39 and every blob written for an
`(org, number)` pair that has no writable row. Nothing is backfilled, so the pattern default stays
in use until a later task re-points the old rows.

Legacy blobs that have no version row (data elements with `currentblobversion IS NULL`) are always
read from the pattern default.

## Who touches the table

```mermaid
flowchart TB
    ops[Operations, psql as platform_storage_admin]
    table[(storage.storageaccounts)]
    cache[Per-pod snapshot in StorageAccountResolver]
    upload[Upload]
    read[Download, cleanup, signing]
    versions[(dataelementblobversions)]
    ops -- "insertstorageaccount(), UPDATE writable" --> table
    table -- "readstorageaccounts() at startup and every 60 s" --> cache
    cache -- "ForWrite(org, number)" --> upload
    cache -- "GetById(id)" --> read
    upload -- "createblobversion_v2 with storageaccountid" --> versions
    versions -- "read functions return storageaccountid" --> read
```

Only operations write the table, with SQL. The service reads it, never writes it. Every version row
written through `createblobversion_v2` carries the id of the account the blob went to.

## The cache

Each pod reads the whole table into memory, at startup and again when the snapshot is older than
`GeneralSettings:StorageAccountCacheSeconds` (default 60). The snapshot is small: one row per account
owned by a service owner, tens of rows at most. No request reads the table directly.

- **Reads are always correct.** Rows are immutable, so a cached row describes the right location for
  as long as the pod runs. An id that is not in the snapshot (a row inserted after the last refresh)
  triggers one refresh, and only then an error if it still does not exist.
- **Writes may lag by one cache period.** After operations insert a writable row, pods that have not
  refreshed yet still send new uploads to the previous target. That is harmless: each version row
  records where its blob actually went, so reads and cleanup follow it. The lag is also short compared
  with an upload, so an exact cut-over time is not needed.
- **An unreadable row never stops the pod.** A row whose `provider` this build does not know, or
  whose document fails validation, is logged as critical at startup and on every refresh. Only the
  operations that need that row fail: reads of blobs in that account, and uploads for its
  `(org, number)` while it is writable. They never fall back to the pattern default, because the
  pattern account does not hold those blobs.

## Resolving the account of a blob

```mermaid
flowchart TD
    start([Data element read with<br>readdataelement_v3 or readinstance_v3]) --> hasVersion{currentblobversion set?}
    hasVersion -- no --> legacy[Pattern default of<br>instance org and the app's number]
    hasVersion -- yes --> hasId{storageaccountid set?}
    hasId -- yes --> cached{id in the snapshot?}
    cached -- yes --> row[Location from the row]
    cached -- no --> refresh[Refresh the snapshot once] --> cached2{id present now?}
    cached2 -- yes --> row
    cached2 -- "unreadable or missing" --> fail[Operation fails with an error]
    hasId -- no --> pattern[Pattern default of the version row's<br>blobstorageorg and storageaccountnumber]
```

The read functions `readdataelement_v3` and `readinstance_v3` join the current version row and return
`storageaccountid`, `blobstorageorg` and `storageaccountnumber` next to the element, so a download
needs no second query. The paged instance list does not load them; an element taken from the list
cannot be used to read a blob, and the resolver refuses it.

Downloads of a specific version (`GET .../data/{id}/versions/{versionId}`), cleanup of detached and
orphaned versions, hard delete, signing and the A2 migration paths all resolve the same way from the
version row they already hold.

## Writing a new blob version

```mermaid
sequenceDiagram
    participant App
    participant Storage
    participant Cache as Snapshot (per pod)
    participant DB as PostgreSQL
    participant Blob as Storage account

    App->>Storage: PUT/POST data element
    Storage->>Cache: ForWrite(org, number)
    Cache-->>Storage: writable row for (org, number), or the pattern default
    Storage->>DB: createblobversion_v2(..., storageaccountid)
    Note over DB: version row exists, detached, with the account id
    Storage->>Blob: write blob {app}/{instance}/data-elements/{versionId}
    alt write fails
        Storage->>Blob: delete the blob if any
        Storage->>DB: delete the version row
        Storage-->>App: error
    else
        Storage->>DB: update data element, attach the version
        Storage-->>App: data element
    end
```

The account is chosen once, before the version row is created, and the row and the blob always
agree. `number` is `Application.StorageAccountNumber`, or 1 when the application has none. With no
writable row for the pair, `storageaccountid` is `NULL` and the blob goes to the pattern default,
which is the behaviour of every environment until operations insert a row.

The file-scan queue message carries the provider name and document of the account the blob was
written to, so the scanner reads from the same location.

## Cleanup

Detached versions, versions orphaned for more than seven days and hard-deleted data elements are
cleaned up per account. The cleanup readers return the version rows grouped by
`(blobstorageorg, storageaccountnumber, storageaccountid)`, and each group is deleted from its own
account. A data element that was overwritten before and after a move therefore has its old versions
deleted from the old account and the new ones from the new account.

The previous release's cleanup functions skip rows with a non-null `storageaccountid`, so a rolling
deploy never deletes from the wrong account.

## Moving an owner's blobs to a new account

Because reads follow the version row, a move is a copy followed by a re-point. Nothing needs to be
switched at one instant, and the old account is never touched by the service after the re-point.

```mermaid
sequenceDiagram
    participant Ops as Operations
    participant DB as PostgreSQL
    participant Old as Old account
    participant New as New account
    participant Pods as Storage pods

    Ops->>New: create the account, grant Storage and file-scan their roles
    Ops->>DB: storage.insertstorageaccount('azure-blob', '{...}', 'ttd', 1)
    Note over DB: new row, writable. The previous writable row loses the flag
    DB-->>Pods: picked up on the next refresh (at most one cache period)
    Note over Pods: new uploads go to the new account with the new id
    Ops->>Old: copy every blob under ttd/ to the new account (azcopy)
    Old-->>New: blobs copied
    Ops->>DB: UPDATE version rows of (ttd, 1) that still name the old location, set the new id
    Note over Pods: reads now follow the new row. The old account is idle
    Ops->>Old: delete or retire, after the audit
```

1. **Create the account** and give Storage's workload identity and file-scan's identity the Storage
   Blob Data Contributor role on it.
2. **Insert the row.** `storage.insertstorageaccount` takes the writable flag over from the previous
   row for the pair in the same transaction. From the next cache refresh, new versions land in the new
   account. Inserting it with `_writable => false` is also fine when the copy should finish first; the
   flag is then set with an `UPDATE` later.
3. **Copy the blobs.** The versioned blob path is the same in every account, so the copy is a prefix
   copy of `{org}/`. Blobs uploaded after step 2 are already in the new account; a second
   incremental copy catches those written between the first copy and step 4.
4. **Re-point the version rows** in batches. The condition on `storageaccountid` keeps the update to
   rows that still name the old location (`NULL` for the pattern default), so rows written to the new
   account are not touched. Every read of those elements now goes to the new account.
5. **Audit and retire.** Confirm each re-pointed version's blob exists in the new account, then
   delete the old account or remove the service's access. Legacy non-versioned blobs are still read
   from the pattern default and must be converted to versions before a pattern account is retired.

Rows are never deleted. A row that no longer receives writes and has no versions pointing at it is
simply unused.

## SQL for operations

```sql
-- Register an account that receives new versions for (ttd, 1).
SELECT storage.insertstorageaccount(
    'azure-blob',
    '{"accountName":"ttdaltinnprodstrg02","container":"ttd-prod-appsdata-blob-db"}'::jsonb,
    'ttd', 1);

-- List the accounts.
SELECT * FROM storage.readstorageaccounts();

-- Where the versions of (ttd, 1) are today; NULL is the pattern default.
-- Scans the version table: run off-peak or on a replica.
SELECT storageaccountid, count(*)
FROM storage.dataelementblobversions
WHERE blobstorageorg = 'ttd' AND COALESCE(storageaccountnumber, 1) = 1
GROUP BY storageaccountid;
```

Before the first row is inserted in an environment where file scanning is on, the scanner must be
released with support for the location in the queue message and verified end to end against a blob
in a named account.
