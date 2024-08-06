CREATE OR REPLACE FUNCTION storage.reada2image(_name TEXT)
    RETURNS TABLE (image BYTEA)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY
    SELECT
        CASE
            WHEN c.image IS NOT NULL THEN c.image
            ELSE (SELECT p.image FROM storage.a2images p WHERE p.id = c.parentid)
        END
    FROM storage.a2images c WHERE c.name = _name; 
END;
$BODY$;