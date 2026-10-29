-- Runs as the container superuser, whatever its name. The admin role owns the database and the
-- storage schema, so the migrations at Storage startup can run as that role.
--
-- Both users get the password "Password", which is what appsettings.json gives Storage in
-- PostgreSqlSettings:StorageDbAdminPwd and PostgreSqlSettings:StorageDbPwd. To use other passwords,
-- change them here and in those two settings. With docker-compose.yml, platform_storage_admin is the
-- container superuser, so its password is POSTGRES_PASSWORD there instead.
DO $$
BEGIN
  CREATE ROLE platform_storage_admin WITH LOGIN PASSWORD 'Password';
EXCEPTION WHEN duplicate_object THEN
  RAISE NOTICE '%, skipping', SQLERRM USING ERRCODE = SQLSTATE;
END $$;

CREATE DATABASE storagedb OWNER platform_storage_admin;
\c storagedb;
CREATE SCHEMA storage AUTHORIZATION platform_storage_admin;

ALTER SYSTEM SET max_connections TO '200';

CREATE ROLE platform_storage WITH LOGIN PASSWORD 'Password';

GRANT USAGE ON SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,REFERENCES,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT ALL ON ALL SEQUENCES IN SCHEMA storage TO platform_storage;
