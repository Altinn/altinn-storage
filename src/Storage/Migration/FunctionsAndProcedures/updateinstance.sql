CREATE OR REPLACE FUNCTION storage.updateinstance_v4(
        _alternateid UUID,
        _toplevelsimpleprops JSONB,
        _datavalues JSONB,
        _completeconfirmations JSONB,
        _presentationtexts JSONB,
        _status JSONB,
        _substatus JSONB,
        _process JSONB,
        _lastchanged TIMESTAMPTZ,
        _taskid TEXT,
        _confirmed BOOLEAN DEFAULT NULL,
        _expectedinstanceversion INT DEFAULT NULL,
        _expectedprocessstateversion INT DEFAULT NULL)
    RETURNS TABLE (updatedInstance JSONB, instanceversion INT, processstateversion INT, currentprocessstatus TEXT, result TEXT)
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _currentInstance JSONB;
    _currentInstanceVersion INT;
    _currentProcessStateVersion INT;
    _currentProcessStatus TEXT;
BEGIN
    SELECT i.instance, i.instance_version, i.process_state_version
        INTO _currentInstance, _currentInstanceVersion, _currentProcessStateVersion
        FROM storage.instances i
        WHERE i.alternateid = _alternateid
        FOR UPDATE;

    IF NOT FOUND
    THEN
        RETURN QUERY SELECT NULL::JSONB, NULL::INT, NULL::INT, NULL::TEXT, 'not_found'::TEXT;
        RETURN;
    END IF;

    _currentProcessStatus := COALESCE(_currentInstance -> 'Process' ->> 'Status', 'idle');

    IF _expectedinstanceversion IS NOT NULL AND _currentInstanceVersion <> _expectedinstanceversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'instance_version_mismatch'::TEXT;
        RETURN;
    END IF;

    IF _expectedprocessstateversion IS NOT NULL AND _currentProcessStateVersion <> _expectedprocessstateversion
    THEN
        RETURN QUERY SELECT NULL::JSONB, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'process_state_version_mismatch'::TEXT;
        RETURN;
    END IF;

    IF _currentProcessStatus <> 'idle'
    THEN
        RETURN QUERY SELECT NULL::JSONB, _currentInstanceVersion, _currentProcessStateVersion, _currentProcessStatus, 'process_status_conflict'::TEXT;
        RETURN;
    END IF;

    _toplevelsimpleprops := COALESCE(_toplevelsimpleprops, '{}'::JSONB) - 'Process';

    IF _datavalues IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _toplevelsimpleprops ||
                    jsonb_strip_nulls(
                        jsonb_set(
                            '{"DataValues":""}',
                            '{DataValues}',
                            CASE WHEN instance -> 'DataValues' IS NOT NULL THEN
                                instance -> 'DataValues' || _datavalues
                            ELSE
                                _datavalues
                            END
                        )
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSIF _presentationtexts IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _toplevelsimpleprops ||
                    jsonb_strip_nulls(
                        jsonb_set(
                            '{"PresentationTexts":""}',
                            '{PresentationTexts}',
                            CASE WHEN instance -> 'PresentationTexts' IS NOT NULL THEN
                                instance -> 'PresentationTexts' || _presentationtexts
                            ELSE
                                _presentationtexts
                            END
                        )
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSIF _completeconfirmations IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _toplevelsimpleprops ||
                    jsonb_set(
                        '{"CompleteConfirmations":""}',
                        '{CompleteConfirmations}',
                        CASE WHEN instance -> 'CompleteConfirmations' IS NOT NULL THEN
                            instance -> 'CompleteConfirmations' || _completeconfirmations
                        ELSE
                            _completeconfirmations
                        END
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSIF _status IS NOT NULL AND _process IS NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Status}',
                        CASE WHEN instance -> 'Status' IS NOT NULL THEN
                            instance -> 'Status' || _status
                        ELSE
                            _status
                        END
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSIF _substatus IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Status, Substatus}',
                        jsonb_strip_nulls(_substatus)
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSIF _process IS NOT NULL AND _status IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Process}',
                        jsonb_strip_nulls(_process)
                    ) ||
                    jsonb_set(
                        '{"Status":""}',
                        '{Status}',
                        CASE WHEN instance -> 'Status' IS NOT NULL THEN
                            instance -> 'Status' || _status
                        ELSE
                            _status
                        END
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                process_state_version = process_state_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END,
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSIF _process IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Process}',
                        jsonb_strip_nulls(_process)
                    ),
                lastchanged = _lastchanged,
                instance_version = instance_version + 1,
                process_state_version = process_state_version + 1,
                confirmed = CASE WHEN _confirmed IS NULL THEN confirmed ELSE _confirmed END,
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance, storage.instances.instance_version, storage.instances.process_state_version, _currentProcessStatus, 'ok'::TEXT;
    ELSE
        RAISE EXCEPTION 'Unexpected parameters to update instance';
    END IF;
END;
$BODY$;
