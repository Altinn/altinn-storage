ALTER TABLE storage.a2xsls ADD COLUMN IF NOT EXISTS xsltype INT NOT NULL DEFAULT -1;
ALTER TABLE storage.a2xsls DROP CONSTRAINT a2xslsalternateid;
ALTER TABLE storage.a2xsls ADD CONSTRAINT a2xslsalternateid UNIQUE (app, org, lformid, pagenumber, language, xsltype);