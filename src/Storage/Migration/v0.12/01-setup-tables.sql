ALTER TABLE storage.a2xsls ADD COLUMN IF NOT EXISTS isportrait BOOL NOT NULL DEFAULT true;
DROP FUNCTION IF EXISTS storage.reada2xsls(_org TEXT, _app TEXT, _lformid INT, _language TEXT, _xsltype INT);