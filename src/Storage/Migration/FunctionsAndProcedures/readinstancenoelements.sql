CREATE OR REPLACE FUNCTION storage.readinstancenoelements_v2(_alternateid UUID)
    RETURNS TABLE (id BIGINT, instance JSONB, instanceversion INT, processstateversion INT)
    LANGUAGE 'plpgsql'

AS $BODY$
BEGIN
RETURN QUERY
    SELECT i.id, i.instance, i.instance_version, i.process_state_version FROM storage.instances i
        WHERE i.alternateid = _alternateid;
END;
$BODY$;
