-- SCHEMA: events

CREATE SCHEMA IF NOT EXISTS storage
AUTHORIZATION platform_storage_admin;

-- Table: delegation.delegationChanges
CREATE TABLE IF NOT EXISTS storage.dataElements
(
	id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateId UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	instance UUID NOT NULL,
	element jsonb NOT NULL
)
TABLESPACE pg_default;

