CREATE OR REPLACE FUNCTION storage.readinstance_v2(_alternateid UUID)
    RETURNS TABLE (id BIGINT, instance JSONB, instanceversion INT, processstateversion INT, element JSONB, currentblobversion UUID)
    LANGUAGE 'plpgsql'

AS $BODY$
BEGIN
RETURN QUERY
    SELECT i.id, i.instance, i.instance_version, i.process_state_version, d.element, d.currentblobversion FROM storage.instances i
        LEFT JOIN storage.dataelements d ON i.id = d.instanceinternalid
        WHERE i.alternateid = _alternateid
        ORDER BY d.id;

END;
$BODY$;
