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
	instance UUID NOT NULL,
	event JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.applications
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateId TEXT UNIQUE NOT NULL,
	org TEXT NOT NULL,
	application JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.texts
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	org TEXT NOT NULL,
	app TEXT NOT NULL,
	language TEXT NOT NULL,
	applicationInternalId BIGINT REFERENCES storage.applications(id) ON DELETE CASCADE,
	textResource JSONB NOT NULL,
	CONSTRAINT textAlternateId UNIQUE (org, app, language)
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.convertionStatus
(
	instanceTs BIGINT NOT NULL,
	instanceEventTs BIGINT NOT NULL,
	dataElementTs BIGINT NOT NULL,
	applicationTs BIGINT NOT NULL,
	textTs BIGINT NOT NULL
)
TABLESPACE pg_default;

CREATE INDEX IF NOT EXISTS dataElements_instanceInternalId ON storage.dataElements(instanceInternalId);
CREATE INDEX IF NOT EXISTS instanceEvents_instance ON storage.instanceEvents(instance);
CREATE INDEX IF NOT EXISTS instances_partyId ON storage.instances(partyId);