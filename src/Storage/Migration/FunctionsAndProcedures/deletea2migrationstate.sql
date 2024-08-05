CREATE OR REPLACE PROCEDURE storage.deletea2migrationstate (_instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	DELETE FROM storage.a2migrationstate WHERE instanceguid = _instanceguid;
END;
$BODY$;