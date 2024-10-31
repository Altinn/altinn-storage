CREATE OR REPLACE PROCEDURE storage.updatemigrationstatecompleted (_instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a1migrationstate SET completed = now()
		WHERE instanceguid = _instanceguid;
	UPDATE storage.a2migrationstate SET completed = now()
		WHERE instanceguid = _instanceguid;
END;
$BODY$;