ALTER TABLE storage.dataelements
ADD COLUMN IF NOT EXISTS currentblobversion UUID NULL;

CREATE TABLE IF NOT EXISTS storage.dataelementblobversions (
    id UUID PRIMARY KEY,
    instanceguid UUID NOT NULL,
    dataelementid UUID NOT NULL,
    appid TEXT NOT NULL,
    blobstorageorg TEXT NOT NULL,
    storageaccountnumber INT NULL,
    created TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    attached BOOLEAN NOT NULL DEFAULT FALSE
)
TABLESPACE pg_default;

CREATE INDEX IF NOT EXISTS dataelementblobversions_dataelementid
ON storage.dataelementblobversions(dataelementid);

CREATE INDEX IF NOT EXISTS dataelementblobversions_attached_instance
ON storage.dataelementblobversions(instanceguid)
WHERE attached = true;

CREATE INDEX IF NOT EXISTS dataelementblobversions_created_unattached
ON storage.dataelementblobversions(created)
WHERE attached = false;

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
