CREATE OR REPLACE PROCEDURE storage.begindatamutation(
    _instanceguid UUID,
    _lockid BIGINT,
    _secrethash BYTEA,
    _timeout INTERVAL,
    INOUT _result TEXT DEFAULT NULL,
    INOUT _requestid BIGINT DEFAULT NULL
)
LANGUAGE plpgsql
AS $BODY$
DECLARE
    _instanceinternalid BIGINT;
    _now TIMESTAMPTZ;
    _lock RECORD;
BEGIN
    SELECT id INTO _instanceinternalid
    FROM storage.instances
    WHERE alternateid = _instanceguid;

    IF _instanceinternalid IS NULL THEN
        _result := 'instance_not_found';
        RETURN;
    END IF;

    PERFORM pg_advisory_xact_lock(_instanceinternalid);

    SELECT clock_timestamp()
    INTO _now;

    -- Clean up expired tracking rows
    DELETE FROM storage.activedatarequests
    WHERE instanceinternalid = _instanceinternalid
    AND timeoutsat <= _now;

    -- Check for an active lock with preventMutations
    SELECT id, secrethash, preventmutations
    INTO _lock
    FROM storage.instancelocks
    WHERE instanceinternalid = _instanceinternalid
    AND lockeduntil > _now;

    IF _lock IS NOT NULL AND _lock.preventmutations THEN
        -- Lock exists with preventMutations=true, validate token
        IF _lockid IS NOT NULL AND _lockid = _lock.id AND _secrethash = _lock.secrethash THEN
            INSERT INTO storage.activedatarequests (instanceinternalid, startedat, timeoutsat)
            VALUES (_instanceinternalid, _now, _now + _timeout)
            RETURNING id INTO _requestid;
            _result := 'ok';
            RETURN;
        ELSE
            _result := 'mutation_blocked';
            RETURN;
        END IF;
    END IF;

    -- No active lock with preventMutations=true
    INSERT INTO storage.activedatarequests (instanceinternalid, startedat, timeoutsat)
    VALUES (_instanceinternalid, _now, _now + _timeout)
    RETURNING id INTO _requestid;
    _result := 'ok';
END;
$BODY$;
