CREATE TABLE IF NOT EXISTS storage.activedatarequests (
    id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    instanceinternalid BIGINT NOT NULL,
    startedat TIMESTAMPTZ NOT NULL,
    timeoutsat TIMESTAMPTZ NOT NULL
)
TABLESPACE pg_default;

CREATE INDEX IF NOT EXISTS activedatarequests_instanceinternalid ON storage.activedatarequests (instanceinternalid);

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
