-- Fail the startup migration fast instead of queuing ALTER TABLE's lock and stalling writers; the pod restarts and retries.
SET LOCAL lock_timeout = '10s';

CREATE TABLE IF NOT EXISTS storage.storageaccounts (
    id SMALLINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    provider TEXT NOT NULL,             -- 'azure-blob', 'file-system', ...; selects the C# type
    providerinfo JSONB NOT NULL,        -- that provider's own properties, no discriminator inside
    org TEXT NOT NULL,                  -- blob storage org
    storageaccountnumber INT NOT NULL,  -- Application.StorageAccountNumber, default 1 when the app has none
    writable BOOLEAN NOT NULL DEFAULT true, -- target for new blob versions of (org, number)
    created TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- At most one account per (org, number) receives new blob versions.
CREATE UNIQUE INDEX IF NOT EXISTS storageaccounts_writable
    ON storage.storageaccounts (org, storageaccountnumber) WHERE writable;

ALTER TABLE storage.dataelementblobversions
    ADD COLUMN IF NOT EXISTS storageaccountid SMALLINT NULL;

-- NOT VALID skips the scan of existing rows, which would hold a lock on this large table for the
-- whole scan; every existing value is NULL, and new and updated rows are still checked.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'dataelementblobversions_storageaccountid_fkey') THEN
        ALTER TABLE storage.dataelementblobversions
            ADD CONSTRAINT dataelementblobversions_storageaccountid_fkey
            FOREIGN KEY (storageaccountid) REFERENCES storage.storageaccounts(id) NOT VALID;
    END IF;
END $$;

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA storage TO platform_storage;
