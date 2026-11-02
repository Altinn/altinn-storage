CREATE OR REPLACE FUNCTION storage.readdataelement_v2(_alternateid UUID)
    RETURNS TABLE (element JSONB, currentblobversion UUID)
    LANGUAGE 'plpgsql'
    
AS $BODY$
BEGIN
RETURN QUERY 
    SELECT d.element, d.currentblobversion
    FROM storage.dataelements d
    WHERE d.alternateid = _alternateid;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readdataelement_v3(_alternateid UUID)
    RETURNS TABLE (
        element JSONB,
        currentblobversion UUID,
        storageaccountid SMALLINT,
        blobstorageorg TEXT,
        blobstorageaccountnumber INT
    )
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
RETURN QUERY
    SELECT
        d.element,
        d.currentblobversion,
        bv.storageaccountid,
        bv.blobstorageorg AS blobstorageorg,
        bv.storageaccountnumber AS blobstorageaccountnumber
    FROM storage.dataelements d
        LEFT JOIN storage.dataelementblobversions bv ON bv.id = d.currentblobversion
    WHERE d.alternateid = _alternateid;

END;
$BODY$;
