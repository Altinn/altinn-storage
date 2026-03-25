CREATE OR REPLACE PROCEDURE storage.acquireinstancelock(
    _instanceinternalid BIGINT,
    _ttl INTERVAL,
    _preventmutations BOOLEAN,
    _lockedby TEXT,
    _secrethash BYTEA,
    INOUT _result TEXT DEFAULT NULL,
    INOUT _id BIGINT DEFAULT NULL
)
LANGUAGE plpgsql
AS $BODY$
DECLARE
    _lock_exists BOOLEAN;
    _active_requests_exist BOOLEAN;
    _now TIMESTAMPTZ;
BEGIN
    PERFORM pg_advisory_xact_lock(_instanceinternalid);

    SELECT clock_timestamp()
    INTO _now;

    SELECT true FROM storage.instancelocks
    WHERE instanceinternalid = _instanceinternalid
    AND lockeduntil > _now
    INTO _lock_exists;

    IF _lock_exists THEN
        _result := 'lock_held';
        RETURN;
    END IF;

    -- Clean up expired active data request rows
    DELETE FROM storage.activedatarequests
    WHERE instanceinternalid = _instanceinternalid
    AND timeoutsat <= _now;

    -- Check for active data requests
    SELECT true FROM storage.activedatarequests
    WHERE instanceinternalid = _instanceinternalid
    LIMIT 1
    INTO _active_requests_exist;

    IF _active_requests_exist THEN
        _result := 'active_requests';
        RETURN;
    END IF;

    INSERT INTO storage.instancelocks (instanceinternalid, lockedat, lockeduntil, preventmutations, lockedby, secrethash)
    VALUES (_instanceinternalid, _now, _now + _ttl, _preventmutations, _lockedby, _secrethash)
    RETURNING id INTO _id;

    _result := 'ok';
END;
$BODY$;
