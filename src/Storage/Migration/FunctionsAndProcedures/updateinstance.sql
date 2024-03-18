CREATE OR REPLACE FUNCTION storage.updateinstance(_alternateid UUID, _instance JSONB, _datavalues JSONB, _completeconfirmations JSONB, _lastchanged TIMESTAMPTZ, _taskid TEXT)
    RETURNS TABLE (updatedInstance JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
    IF _datavalues IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _instance ||
                    jsonb_set(
                        '{"DataValues":""}',
                        '{DataValues}',
                        CASE WHEN instance -> 'DataValues' IS NOT NULL THEN
                            instance -> 'DataValues' || _datavalues
                        ELSE
                            _datavalues
                        END
                    ),
                lastchanged = _lastchanged,
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _completeconfirmations IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _instance ||
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
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSE
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance || _instance,
                lastchanged = _lastchanged,
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;
    END IF;
END;
$BODY$;
