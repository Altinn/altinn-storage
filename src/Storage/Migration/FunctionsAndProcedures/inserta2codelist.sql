CREATE OR REPLACE PROCEDURE storage.inserta2codelist (_name TEXT, _language TEXT, _version INT, _codelist TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    INSERT INTO storage.a2codelists (name, language, version, codelist) VALUES
        (_name, _language, _version, _codelist)
		ON CONFLICT (name, language, version) DO UPDATE SET codelist = _codelist;	
END;
$BODY$;