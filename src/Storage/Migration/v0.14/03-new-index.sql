CREATE INDEX IF NOT EXISTS instances_lastchanged_filtered ON storage.instances(lastChanged)
	WHERE (instance -> 'Status' -> 'IsArchived')::BOOLEAN = false;