DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_attribute
        WHERE attrelid = 'storage.dataelements'::regclass
          AND attname = 'currentblobversion'
          AND attnum > 0 AND NOT attisdropped
    ) THEN
        ALTER TABLE storage.dataelements
        ADD COLUMN IF NOT EXISTS currentblobversion UUID NULL;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS storage.dataelementblobversions (
    id UUID PRIMARY KEY,
    instanceguid UUID NOT NULL,
    dataelementid UUID NOT NULL,
    appid TEXT NOT NULL,
    blobstorageorg TEXT NOT NULL,
    storageaccountnumber INT NULL,
    created TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    detachedat TIMESTAMPTZ NULL DEFAULT NOW()
)
TABLESPACE pg_default;

-- At most one version per data element is attached. The unique index enforces that
-- invariant; since unique indexes are checked per statement, a supersede has to detach
-- the previous version before the statement that attaches its replacement.
CREATE UNIQUE INDEX IF NOT EXISTS dataelementblobversions_attached_instance_element
ON storage.dataelementblobversions(instanceguid, dataelementid)
WHERE detachedat IS NULL;

-- The index above holds one entry per live data element and is written once per version,
-- at attach time. Detached rows are a small rolling window of pending uploads and
-- versions still inside the cleanup grace period, so splitting the remaining access paths
-- on detachedat keeps the upload path off the large index.
CREATE INDEX IF NOT EXISTS dataelementblobversions_detached_dataelement
ON storage.dataelementblobversions(dataelementid)
WHERE detachedat IS NOT NULL;

CREATE INDEX IF NOT EXISTS dataelementblobversions_detachedat
ON storage.dataelementblobversions(detachedat)
WHERE detachedat IS NOT NULL;

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
