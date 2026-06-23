DROP TABLE IF EXISTS storage.dataelementblobversionstate;
DROP TABLE IF EXISTS storage.dataelementblobversions;

ALTER TABLE storage.dataelements
DROP COLUMN IF EXISTS currentblobversion;

ALTER TABLE storage.dataelements
ADD COLUMN currentblobversion UUID NULL;

CREATE TABLE storage.dataelementblobversions (
    id UUID PRIMARY KEY,
    instanceguid UUID NOT NULL,
    dataelementid UUID NOT NULL,
    appid TEXT NOT NULL,
    blobstorageorg TEXT NOT NULL,
    storageaccountnumber INT NULL,
    created TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    attached TIMESTAMPTZ NULL
)
TABLESPACE pg_default;

CREATE INDEX dataelementblobversions_dataelementid
ON storage.dataelementblobversions(dataelementid, created);

CREATE INDEX dataelementblobversions_attached_instance
ON storage.dataelementblobversions(instanceguid, created)
WHERE attached IS NOT NULL;

CREATE INDEX dataelementblobversions_unattached
ON storage.dataelementblobversions(created, instanceguid)
WHERE attached IS NULL;

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
