CREATE OR REPLACE PROCEDURE storage.inserta2migrationstate (_a2archiveReference BIGINT)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
	INSERT INTO storage.a2migrationstate (a2archivereference) VALUES
		(_a2archiveReference)
	ON CONFLICT (a2archivereference) DO NOTHING;
END;
$BODY$;