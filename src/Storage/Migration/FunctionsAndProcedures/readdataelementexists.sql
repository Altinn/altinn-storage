CREATE OR REPLACE FUNCTION storage.readdataelementexists(_alternateid UUID)
    RETURNS TABLE (element BOOLEAN)
    LANGUAGE 'plpgsql'

AS $BODY$
BEGIN
RETURN QUERY
    SELECT EXISTS (
        SELECT 1
        FROM storage.dataelements de where de.alternateid = _alternateid
    );
END;
$BODY$;
