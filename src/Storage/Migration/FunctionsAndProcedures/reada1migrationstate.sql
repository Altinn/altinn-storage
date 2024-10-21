CREATE OR REPLACE FUNCTION storage.reada1migrationstate(_a1archivereference BIGINT)
    RETURNS TABLE (instanceguid UUID)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ms.instanceguid FROM storage.a1migrationstate ms WHERE ms.a1archivereference = _a1archivereference;
END;
$BODY$;