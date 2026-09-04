DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_attribute
        WHERE attrelid = 'storage.instances'::regclass
          AND attname = 'instance_version'
          AND attnum > 0 AND NOT attisdropped
    ) THEN
        ALTER TABLE storage.instances
        ADD COLUMN IF NOT EXISTS instance_version INT NOT NULL DEFAULT 1;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_attribute
        WHERE attrelid = 'storage.instances'::regclass
          AND attname = 'process_state_version'
          AND attnum > 0 AND NOT attisdropped
    ) THEN
        ALTER TABLE storage.instances
        ADD COLUMN IF NOT EXISTS process_state_version INT NOT NULL DEFAULT 1;
    END IF;
END $$;
