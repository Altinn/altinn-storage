CREATE OR REPLACE FUNCTION storage.reada2migrationstate(_a2archivereference BIGINT)
    RETURNS TABLE (instanceguid UUID)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ms.instanceguid FROM storage.a2migrationstate ms WHERE ms.a2archivereference = _a2archivereference;
END;
$BODY$;