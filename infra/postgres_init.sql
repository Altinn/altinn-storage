CREATE DATABASE storagedb;
\c storagedb;
CREATE SCHEMA storage;

ALTER SYSTEM SET max_connections TO '200';

CREATE ROLE platform_storage WITH LOGIN PASSWORD 'Password';

GRANT USAGE ON SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,REFERENCES,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT ALL ON ALL SEQUENCES IN SCHEMA storage TO platform_storage;
