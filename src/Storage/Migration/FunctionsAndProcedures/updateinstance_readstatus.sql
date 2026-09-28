CREATE OR REPLACE FUNCTION storage.updateinstance_readstatus(
        _alternateid UUID,
        _readstatus INT)
    RETURNS TABLE (updatedInstance JSONB, instanceversion INT, processstateversion INT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        UPDATE storage.instances SET
            instance = jsonb_set(
                instance,
                '{Status}',
                CASE WHEN jsonb_typeof(instance -> 'Status') = 'object' THEN
                    instance -> 'Status' || jsonb_build_object('ReadStatus', _readstatus)
                ELSE
                    jsonb_build_object('ReadStatus', _readstatus)
                END
            )
        WHERE _alternateid = alternateid
        RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, 'ok'::TEXT;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::INT, NULL::INT, 'not_found'::TEXT;
    END IF;
END;
$BODY$;
