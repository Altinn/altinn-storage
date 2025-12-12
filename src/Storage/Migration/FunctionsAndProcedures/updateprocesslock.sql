CREATE OR REPLACE PROCEDURE storage.updateprocesslock(
    _id UUID,
    _instanceinternalid BIGINT,
    _ttl INTERVAL,
    INOUT _result TEXT DEFAULT NULL
)
LANGUAGE plpgsql
AS $BODY$
DECLARE
    _locked_until TIMESTAMPTZ;
    _now TIMESTAMPTZ;
BEGIN
    PERFORM pg_advisory_xact_lock(_instanceinternalid);

    SELECT lockeduntil FROM storage.instancelocks
    WHERE id = _id
    AND instanceinternalid = _instanceinternalid
    INTO _locked_until;

    IF _locked_until IS null THEN
        _result := 'lock_not_found';
        RETURN;
    END IF;

    SELECT clock_timestamp()
    INTO _now;

    IF _locked_until <= _now THEN
        _result := 'lock_expired';
        RETURN;
    END IF;

    UPDATE storage.instancelocks
    SET lockeduntil = _now + _ttl
    WHERE id = _id;

    _result := 'ok';
END;
$BODY$;
