CREATE OR REPLACE FUNCTION storage.readdataelement_v2(_alternateid UUID)
    RETURNS TABLE (element JSONB, currentblobversion UUID)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT d.element, d.currentblobversion
    FROM storage.dataelements d
    WHERE d.alternateid = _alternateid;

END;
$BODY$;
