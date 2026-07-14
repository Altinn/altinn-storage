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
    attached BOOLEAN NOT NULL DEFAULT FALSE
)
TABLESPACE pg_default;

CREATE INDEX dataelementblobversions_dataelementid
ON storage.dataelementblobversions(dataelementid, created);

CREATE INDEX dataelementblobversions_attached_instance
ON storage.dataelementblobversions(instanceguid, created)
WHERE attached = true;

CREATE INDEX dataelementblobversions_unattached
ON storage.dataelementblobversions(created, instanceguid)
WHERE attached = false;

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
