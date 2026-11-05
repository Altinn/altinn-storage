CREATE OR REPLACE FUNCTION storage.readstorageaccounts()
    RETURNS TABLE (
        id SMALLINT,
        provider TEXT,
        providerinfo JSONB,
        org TEXT,
        storageaccountnumber INT,
        writable BOOLEAN,
        created TIMESTAMPTZ
    )
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        SELECT
            sa.id,
            sa.provider,
            sa.providerinfo,
            sa.org,
            sa.storageaccountnumber,
            sa.writable,
            sa.created
        FROM storage.storageaccounts sa
        ORDER BY sa.id;
END;
$BODY$;
