ALTER TABLE storage.instancelocks ADD COLUMN IF NOT EXISTS preventmutations BOOLEAN NOT NULL DEFAULT false;
