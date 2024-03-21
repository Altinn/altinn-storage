CREATE OR REPLACE FUNCTION storage.readinstancenoelements(_alternateid UUID)
    RETURNS TABLE (id BIGINT, instance JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT i.id, i.instance FROM storage.instances i
        WHERE i.alternateid = _alternateid;
END;
$BODY$;