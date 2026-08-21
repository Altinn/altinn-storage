CREATE OR REPLACE FUNCTION storage.deleteinstancemutationidempotency(_createdbefore TIMESTAMPTZ, _batchsize INT)
    RETURNS INT
    LANGUAGE 'plpgsql'
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    DELETE FROM storage.instance_mutation_idempotency
        WHERE ctid IN (
            SELECT ctid
            FROM storage.instance_mutation_idempotency
            WHERE created < _createdbefore
            LIMIT _batchsize
        );
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;
