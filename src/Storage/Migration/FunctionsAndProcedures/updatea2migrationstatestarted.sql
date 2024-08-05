CREATE OR REPLACE PROCEDURE storage.updatea2migrationstatestarted (_a2archivereference BIGINT, _instanceguid UUID)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	UPDATE storage.a2migrationstate SET instanceguid = _instanceguid, started = now()
		WHERE a2archivereference = _a2archivereference;
END;
$BODY$;