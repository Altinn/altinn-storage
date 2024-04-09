CREATE OR REPLACE FUNCTION storage.readinstanceevent(_alternateid UUID)
    RETURNS TABLE (event JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT ie.event FROM storage.instanceevents ie WHERE alternateid = _alternateid;

END;
$BODY$;