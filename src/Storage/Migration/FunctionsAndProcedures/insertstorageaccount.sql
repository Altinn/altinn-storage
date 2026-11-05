CREATE OR REPLACE FUNCTION storage.insertstorageaccount(
    _provider TEXT,
    _providerinfo JSONB,
    _org TEXT,
    _storageaccountnumber INT,
    _writable BOOLEAN DEFAULT true)
    RETURNS SMALLINT
    LANGUAGE plpgsql
AS $BODY$
DECLARE
    _id SMALLINT;
BEGIN
    -- At most one row per (org, number) is writable, so a new writable row takes the flag over.
    IF _writable THEN
        UPDATE storage.storageaccounts
        SET writable = false
        WHERE org = _org
            AND storageaccountnumber = _storageaccountnumber
            AND writable;
    END IF;

    INSERT INTO storage.storageaccounts (provider, providerinfo, org, storageaccountnumber, writable)
    VALUES (_provider, _providerinfo, _org, _storageaccountnumber, _writable)
    RETURNING id INTO _id;

    RETURN _id;
END;
$BODY$;
