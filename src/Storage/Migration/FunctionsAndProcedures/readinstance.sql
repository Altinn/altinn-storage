CREATE OR REPLACE FUNCTION storage.readinstance_v2(_alternateid UUID)
    RETURNS TABLE (id BIGINT, instance JSONB, instanceversion INT, processstateversion INT, element JSONB, currentblobversion UUID)
    LANGUAGE 'plpgsql'

AS $BODY$
BEGIN
RETURN QUERY
    SELECT i.id, i.instance, i.instance_version, i.process_state_version, d.element, d.currentblobversion FROM storage.instances i
        LEFT JOIN storage.dataelements d ON i.id = d.instanceinternalid
        WHERE i.alternateid = _alternateid
        ORDER BY d.id;

END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.readinstance_v3(_alternateid UUID)
    RETURNS TABLE (
        id BIGINT,
        instance JSONB,
        instanceversion INT,
        processstateversion INT,
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
        i.id,
        i.instance,
        i.instance_version,
        i.process_state_version,
        d.element,
        d.currentblobversion,
        bv.storageaccountid,
        bv.blobstorageorg AS blobstorageorg,
        bv.storageaccountnumber AS blobstorageaccountnumber
    FROM storage.instances i
        LEFT JOIN storage.dataelements d ON i.id = d.instanceinternalid
        LEFT JOIN storage.dataelementblobversions bv ON bv.id = d.currentblobversion
        WHERE i.alternateid = _alternateid
        ORDER BY d.id;

END;
$BODY$;
