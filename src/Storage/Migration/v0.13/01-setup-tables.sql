ALTER TABLE storage.instances ADD COLUMN IF NOT EXISTS altinnmainversion SMALLINT NOT NULL DEFAULT 3;
CREATE INDEX IF NOT EXISTS instances_partyid_altinnmainversion_lastchanged ON storage.instances(partyId, altinnmainversion, lastChanged);