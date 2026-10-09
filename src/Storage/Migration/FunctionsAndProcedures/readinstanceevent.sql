CREATE OR REPLACE FUNCTION storage.readinstanceevent_v2(_instance UUID, _alternateid UUID)
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ie.event FROM storage.instanceevents ie
        WHERE ie.alternateid = _alternateid AND ie.instance = _instance;

END;
$BODY$;
