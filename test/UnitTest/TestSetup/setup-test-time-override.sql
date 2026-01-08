CREATE SCHEMA IF NOT EXISTS test_override;

CREATE TABLE IF NOT EXISTS test_override.frozen_time (
    id INTEGER PRIMARY KEY DEFAULT 1,
    frozen_at TIMESTAMPTZ,
    CONSTRAINT single_row CHECK (id = 1)
);

INSERT INTO test_override.frozen_time (id, frozen_at) 
VALUES (1, NULL) 
ON CONFLICT (id) DO NOTHING;

CREATE OR REPLACE FUNCTION test_override.now()
RETURNS TIMESTAMPTZ
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        (SELECT frozen_at FROM test_override.frozen_time WHERE id = 1),
        pg_catalog.now()
    );
$$;

CREATE OR REPLACE FUNCTION test_override.clock_timestamp()
RETURNS TIMESTAMPTZ
LANGUAGE sql
AS $$
    SELECT COALESCE(
        (SELECT frozen_at FROM test_override.frozen_time WHERE id = 1),
        pg_catalog.clock_timestamp()
    );
$$;

CREATE OR REPLACE FUNCTION test_override.freeze_time(_frozen_at TIMESTAMPTZ)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE test_override.frozen_time
    SET frozen_at = _frozen_at
    WHERE id = 1;
END;
$$;

CREATE OR REPLACE FUNCTION test_override.unfreeze_time()
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE test_override.frozen_time
    SET frozen_at = NULL
    WHERE id = 1;
END;
$$;

GRANT USAGE ON SCHEMA test_override TO platform_storage;
GRANT SELECT, UPDATE ON ALL TABLES IN SCHEMA test_override TO platform_storage;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA test_override TO platform_storage;

ALTER DATABASE storagedb
SET search_path = test_override, pg_temp, pg_catalog, "$user", public;
