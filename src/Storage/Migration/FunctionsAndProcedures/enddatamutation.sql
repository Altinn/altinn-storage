CREATE OR REPLACE PROCEDURE storage.enddatamutation(
    _requestid BIGINT
)
LANGUAGE plpgsql
AS $BODY$
BEGIN
    DELETE FROM storage.activedatarequests WHERE id = _requestid;
END;
$BODY$;
