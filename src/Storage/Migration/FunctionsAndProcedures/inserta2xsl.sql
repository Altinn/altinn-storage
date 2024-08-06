CREATE OR REPLACE PROCEDURE storage.inserta2xsl (_org TEXT, _app TEXT, _lformid INT, _language TEXT, _pagenumber INT, _xsl TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _parentId INTEGER;
    _appId INTEGER;
    _applicationinternalid INTEGER;
BEGIN
    SELECT id into _applicationinternalid FROM storage.applications WHERE org = _org AND app = _app;
    SELECT id into _parentId FROM storage.a2xsls WHERE org = _org AND app = _app AND xsl = _xsl;
    SELECT id into _appId FROM storage.applications WHERE org = _org AND app = _app;
    IF _parentId IS NOT NULL THEN
        INSERT INTO storage.a2xsls (org, app, applicationinternalid, parentid, lformid, language, pagenumber, xsl) VALUES
            (_org, _app, _applicationinternalid, _parentId, _lformid, _language, _pagenumber, NULL)
            ON CONFLICT (app, org, lformid, pagenumber, language) DO NOTHING;
    ELSE
        INSERT INTO storage.a2xsls (org, app, applicationinternalid, parentid, lformid, language, pagenumber, xsl) VALUES
            (_org, _app, _applicationinternalid, NULL, _lformid, _language, _pagenumber, _xsl)
            ON CONFLICT (app, org, lformid, pagenumber, language) DO UPDATE SET xsl = _xsl;			
    END IF;
END;
$BODY$;