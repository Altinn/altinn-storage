CREATE OR REPLACE PROCEDURE storage.acquireinstancelock(
    _instanceinternalid BIGINT,
    _ttl INTERVAL,
    _lockedby TEXT,
    _secrethash BYTEA,
    INOUT _result TEXT DEFAULT NULL,
    INOUT _id BIGINT DEFAULT NULL
)
LANGUAGE plpgsql
AS $BODY$
DECLARE
    _lock_exists BOOLEAN;
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

    INSERT INTO storage.instancelocks (instanceinternalid, lockedat, lockeduntil, lockedby, secrethash)
    VALUES (_instanceinternalid, _now, _now + _ttl, _lockedby, _secrethash)
    RETURNING id INTO _id;

    _result := 'ok';
END;
$BODY$;
