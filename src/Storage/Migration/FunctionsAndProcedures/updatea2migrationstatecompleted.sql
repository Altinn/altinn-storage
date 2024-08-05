CREATE OR REPLACE PROCEDURE storage.updatea2migrationstatecompleted (_instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a2migrationstate SET completed = now()
		WHERE instanceguid = _instanceguid;
END;
$BODY$;