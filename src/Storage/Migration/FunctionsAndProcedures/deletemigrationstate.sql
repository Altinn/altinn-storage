CREATE OR REPLACE PROCEDURE storage.deletemigrationstate (_instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	DELETE FROM storage.a1migrationstate WHERE instanceguid = _instanceguid;
	DELETE FROM storage.a2migrationstate WHERE instanceguid = _instanceguid;
END;
$BODY$;