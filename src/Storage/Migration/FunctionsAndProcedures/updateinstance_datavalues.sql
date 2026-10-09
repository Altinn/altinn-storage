CREATE OR REPLACE FUNCTION storage.updateinstance_datavalues(
    _alternateid UUID,
    _datavalues JSONB,
    _expectedinstanceversion INT DEFAULT NULL,
    _expectedprocessstateversion INT DEFAULT NULL)
    RETURNS TABLE (
        result TEXT,
        id BIGINT,
        instance JSONB,
        instanceversion INT,
        processstateversion INT,
        element JSONB,
        currentblobversion UUID)
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _currentinstanceversion INT;
    _currentprocessstateversion INT;
BEGIN
    SELECT i.instance_version, i.process_state_version
    INTO _currentinstanceversion, _currentprocessstateversion
    FROM storage.instances i
    WHERE i.alternateid = _alternateid
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN QUERY SELECT 'not_found'::TEXT, NULL::BIGINT, NULL::JSONB, NULL::INT, NULL::INT, NULL::JSONB, NULL::UUID;
        RETURN;
    END IF;

    IF _expectedinstanceversion IS NOT NULL AND _currentinstanceversion <> _expectedinstanceversion THEN
        RETURN QUERY SELECT 'instance_version_mismatch'::TEXT, NULL::BIGINT, NULL::JSONB, _currentinstanceversion, _currentprocessstateversion, NULL::JSONB, NULL::UUID;
        RETURN;
    END IF;

    IF _expectedprocessstateversion IS NOT NULL AND _currentprocessstateversion <> _expectedprocessstateversion THEN
        RETURN QUERY SELECT 'process_state_version_mismatch'::TEXT, NULL::BIGINT, NULL::JSONB, _currentinstanceversion, _currentprocessstateversion, NULL::JSONB, NULL::UUID;
        RETURN;
    END IF;

    UPDATE storage.instances i
    SET instance = jsonb_set(
        i.instance,
        '{DataValues}',
        jsonb_strip_nulls(COALESCE(NULLIF(i.instance -> 'DataValues', 'null'::JSONB), '{}'::JSONB) || _datavalues))
    WHERE i.alternateid = _alternateid;

    RETURN QUERY
        SELECT 'ok'::TEXT, r.id, r.instance, r.instanceversion, r.processstateversion, r.element, r.currentblobversion
        FROM storage.readinstance_v2(_alternateid) r;
END;
$BODY$;
