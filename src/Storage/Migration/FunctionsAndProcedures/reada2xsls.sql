CREATE OR REPLACE FUNCTION storage.reada2xsls(_org TEXT, _app TEXT, _lformid INT, _language TEXT, _xsltype INT)
    RETURNS TABLE (xsl TEXT)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT
        CASE
            WHEN x.xsl IS NOT NULL THEN x.xsl
            ELSE (SELECT p.xsl FROM storage.a2xsls p WHERE p.id = x.parentid)
        END
        FROM storage.a2xsls x
        WHERE x.org = _org and x.app = _app and x.lformid = _lformid and x.language = _language and x.xsltype = _xsltype
        ORDER BY pagenumber;

END;
$BODY$;