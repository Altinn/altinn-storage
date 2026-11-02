CREATE TABLE IF NOT EXISTS storage.storageaccounts (
    id SMALLINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    providerinfo JSONB NOT NULL,        -- provider-specific document, discriminated by "provider"
    org TEXT NOT NULL,                  -- blob storage org
    storageaccountnumber INT NOT NULL,  -- Application.StorageAccountNumber, default 1 when the app has none
    writable BOOLEAN NOT NULL DEFAULT true, -- target for new blob versions of (org, number)
    created TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- At most one account per (org, number) receives new blob versions.
CREATE UNIQUE INDEX IF NOT EXISTS storageaccounts_writable
    ON storage.storageaccounts (org, storageaccountnumber) WHERE writable;

ALTER TABLE storage.dataelementblobversions
    ADD COLUMN IF NOT EXISTS storageaccountid SMALLINT NULL REFERENCES storage.storageaccounts(id);

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA storage TO platform_storage;
