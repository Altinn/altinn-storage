CREATE SCHEMA IF NOT EXISTS storage
AUTHORIZATION platform_storage_admin;

CREATE TABLE IF NOT EXISTS storage.instances
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateid UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	partyid BIGINT NOT NULL,
	created TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	lastchanged TIMESTAMPTZ NOT NULL DEFAULT NOW(),
	org TEXT NOT NULL,
	appid TEXT NOT NULL,
	taskid TEXT NULL,
	instance JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.dataElements
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateid UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	instanceguid UUID NOT NULL,
	instanceinternalid BIGINT REFERENCES storage.instances(id) ON DELETE CASCADE,
	element JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.instanceEvents
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	alternateid UUID UNIQUE NOT NULL DEFAULT gen_random_uuid(),
	instance UUID NOT NULL,
	event JSONB NOT NULL
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.applications
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	app TEXT NOT NULL,
	org TEXT NOT NULL,
	application JSONB NOT NULL,
	CONSTRAINT app_org UNIQUE (org, app)
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.texts
(
	id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	org TEXT NOT NULL,
	app TEXT NOT NULL,
	language TEXT NOT NULL,
	applicationinternalid BIGINT REFERENCES storage.applications(id) ON DELETE CASCADE,
	textresource JSONB NOT NULL,
	CONSTRAINT textalternateid UNIQUE (org, app, language)
)
TABLESPACE pg_default;

CREATE TABLE IF NOT EXISTS storage.convertionStatus
(
	instancets BIGINT NOT NULL,
	instanceeventts BIGINT NOT NULL,
	dataelementts BIGINT NOT NULL,
	applicationts BIGINT NOT NULL,
	textts BIGINT NOT NULL
)
TABLESPACE pg_default;

CREATE INDEX IF NOT EXISTS dataelements_instanceinternalId ON storage.dataelements(instanceInternalId);
CREATE INDEX IF NOT EXISTS dataelements_instanceguid ON storage.dataelements(instanceGuid);
CREATE INDEX IF NOT EXISTS dataelements_isharddeleted ON storage.dataelements(id)
    WHERE (element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN;
CREATE INDEX IF NOT EXISTS instances_isharddeleted_and_more ON storage.instances(id)
	WHERE (instance -> 'Status' -> 'IsHardDeleted')::BOOLEAN AND
	(
		NOT (instance -> 'Status' -> 'IsArchived')::BOOLEAN OR (instance -> 'CompleteConfirmations') IS NOT NULL
	);

CREATE INDEX IF NOT EXISTS instanceevents_instance ON storage.instanceevents(instance);

CREATE INDEX IF NOT EXISTS instances_partyid ON storage.instances(partyId);
CREATE INDEX IF NOT EXISTS instances_appid ON storage.instances(appId);
CREATE INDEX IF NOT EXISTS instances_appid_taskId ON storage.instances(appId, taskId);
CREATE INDEX IF NOT EXISTS instances_appid_lastchanged ON storage.instances(appId, lastChanged);
CREATE INDEX IF NOT EXISTS instances_partyid_lastchanged ON storage.instances(partyId, lastChanged);
CREATE INDEX IF NOT EXISTS instances_lastchanged ON storage.instances(lastChanged);
CREATE INDEX IF NOT EXISTS instances_org ON storage.instances(org);
CREATE INDEX IF NOT EXISTS instances_created ON storage.instances (created);
CREATE INDEX IF NOT EXISTS instances_isharddeleted_confirmed ON storage.instances(id) WHERE (instance -> 'Status' -> 'IsHardDeleted')::BOOLEAN AND (instance -> 'CompleteConfirmations') IS NOT NULL;

CREATE INDEX IF NOT EXISTS applications_org ON storage.applications(org);
CREATE INDEX IF NOT EXISTS applications_app ON storage.applications(app);