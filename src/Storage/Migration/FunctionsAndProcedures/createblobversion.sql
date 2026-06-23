CREATE OR REPLACE FUNCTION storage.createblobversion(
    _id UUID,
    _instanceguid UUID,
    _dataelementid UUID,
    _appid TEXT,
    _blobstorageorg TEXT,
    _storageaccountnumber INT)
    RETURNS void
    LANGUAGE plpgsql
AS $BODY$
BEGIN
    INSERT INTO storage.dataelementblobversions(
        id,
        instanceguid,
        dataelementid,
        appid,
        blobstorageorg,
        storageaccountnumber)
    VALUES (
        _id,
        _instanceguid,
        _dataelementid,
        _appid,
        _blobstorageorg,
        _storageaccountnumber);
END;
$BODY$;
