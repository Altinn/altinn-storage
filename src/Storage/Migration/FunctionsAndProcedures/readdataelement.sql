CREATE OR REPLACE FUNCTION storage.readdataelement(_alternateid UUID)
    RETURNS TABLE (element JSONB)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT d.element FROM storage.dataelements d WHERE alternateid = _alternateid;

END;
$BODY$;