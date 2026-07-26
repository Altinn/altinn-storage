CREATE OR REPLACE PROCEDURE storage.raiseinstancemutationerror(
    _code TEXT,
    _currentinstanceversion INT,
    _currentprocessstateversion INT,
    _dataelementid UUID DEFAULT NULL,
    _currentprocessstatus TEXT DEFAULT NULL)
LANGUAGE plpgsql
AS $BODY$
BEGIN
    RAISE EXCEPTION USING
        ERRCODE = 'AM001',
        MESSAGE = json_build_object(
            'code', _code,
            'currentInstanceVersion', _currentinstanceversion,
            'currentProcessStateVersion', _currentprocessstateversion,
            'currentProcessStatus', _currentprocessstatus,
            'dataElementId', _dataelementid)::TEXT;
END;
$BODY$;
