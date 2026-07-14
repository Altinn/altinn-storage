CREATE TABLE IF NOT EXISTS storage.instance_mutation_idempotency
(
    idempotency_key UUID PRIMARY KEY,
    instance UUID NOT NULL,
    previous_instance_version INT NOT NULL,
    produced_instance_version INT NOT NULL,
    created_data_element_ids TEXT[] NOT NULL DEFAULT '{}',
    created TIMESTAMPTZ NOT NULL DEFAULT now()
);

GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage;
GRANT SELECT,INSERT,UPDATE,REFERENCES,DELETE,TRUNCATE,TRIGGER ON ALL TABLES IN SCHEMA storage TO platform_storage_admin;
