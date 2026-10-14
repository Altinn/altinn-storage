CREATE TABLE IF NOT EXISTS storage.parties
(
    partyid BIGINT PRIMARY KEY,
    partyuuid UUID NOT NULL,
    personnumber TEXT,
    organisationnumber TEXT,
    username TEXT,
    externalidentifier TEXT,
    updated TIMESTAMPTZ NOT NULL DEFAULT now()
);

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
