CREATE OR REPLACE FUNCTION storage.updateinstance_v2(
        _alternateid UUID,
        _toplevelsimpleprops JSONB,
        _datavalues JSONB,
        _completeconfirmations JSONB,
        _presentationtexts JSONB,
        _status JSONB,
        _substatus JSONB,
        _process JSONB,
        _lastchanged TIMESTAMPTZ,
        _taskid TEXT)
    RETURNS TABLE (updatedInstance JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
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
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
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
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
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
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
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
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
    ELSIF _substatus IS NOT NULL THEN
        RETURN QUERY
            UPDATE storage.instances SET
                instance = instance ||
                    jsonb_set(
                        instance || _toplevelsimpleprops,
                        '{Status, Substatus}',
                        jsonb_strip_nulls(_substatus)
                    ),
                lastchanged = _lastchanged
            WHERE _alternateid = alternateid
            RETURNING instance;
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
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;
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
                taskid = _taskid
            WHERE _alternateid = alternateid
            RETURNING instance;                
    ELSE
        RAISE EXCEPTION 'Unexpected parameters to update instance';
    END IF;
END;
$BODY$;
