CREATE OR REPLACE FUNCTION storage.reada2codelist(_name TEXT, _language TEXT)
    RETURNS TABLE (codelist TEXT)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    SELECT c.codelist FROM storage.a2codelists c
        WHERE name = _name AND language = _language
        ORDER BY version DESC LIMIT 1;
END;
$BODY$;