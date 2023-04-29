CREATE SCHEMA IF NOT EXISTS storage
AUTHORIZATION platform_storage_admin;

CREATE TABLE IF NOT EXISTS storage.instances
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateId UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	partyId BIGINT NOT NULL,
	instance JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.dataElements
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateId UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	instanceGuid UUID NOT NULL,
	instanceInternalId BIGINT REFERENCES storage.instances(id) ON DELETE CASCADE,
	element JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.instanceEvents
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateId UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	instance UUID REFERENCES storage.instances(alternateId) ON DELETE CASCADE,
	event JSONB NOT NULL
)
TABLESPACE pg_default;

