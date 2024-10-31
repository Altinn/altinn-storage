CREATE OR REPLACE PROCEDURE storage.inserta1migrationstate (_a1archiveReference BIGINT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	INSERT INTO storage.a1migrationstate (a1archivereference) VALUES
		(_a1archiveReference)
	ON CONFLICT (a1archivereference) DO NOTHING;
END;
$BODY$;