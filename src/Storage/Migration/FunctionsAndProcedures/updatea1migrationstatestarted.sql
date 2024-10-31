CREATE OR REPLACE PROCEDURE storage.updatea1migrationstatestarted (_a1archivereference BIGINT, _instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a1migrationstate SET instanceguid = _instanceguid, started = now()
		WHERE a1archivereference = _a1archivereference;
END;
$BODY$;