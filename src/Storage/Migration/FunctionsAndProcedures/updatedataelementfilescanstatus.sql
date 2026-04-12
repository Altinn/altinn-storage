CREATE OR REPLACE FUNCTION storage.updatedataelementfilescanstatus(
    _dataelementGuid UUID,
    _instanceGuid UUID,
    _fileScanResult TEXT,
    _blobVersionId TEXT)
    RETURNS TABLE (updatedElement JSONB)
    LANGUAGE 'plpgsql'
AS $BODY$
BEGIN
    RETURN QUERY
        UPDATE storage.dataelements
            SET element = jsonb_set(element, '{FileScanResult}', to_jsonb(_fileScanResult), true)
            WHERE alternateid = _dataelementGuid
                AND instanceguid = _instanceGuid
                AND (_blobVersionId IS NULL OR _blobVersionId = '' OR element ->> 'BlobVersionId' = _blobVersionId)
            RETURNING element;
END;
$BODY$;
