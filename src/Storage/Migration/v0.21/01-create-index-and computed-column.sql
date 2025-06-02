ALTER TABLE storage.instances ADD COLUMN IF NOT EXISTS confirmed bool;

CREATE INDEX IF NOT EXISTS instances_lastchanged_org_not_confirmed_by_org ON storage.instances(org, lastchanged desc, id) WHERE confirmed = False;