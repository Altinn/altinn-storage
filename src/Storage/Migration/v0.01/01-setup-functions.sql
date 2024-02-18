CREATE OR REPLACE PROCEDURE storage.insertdataelement(
	IN _instanceinternalid bigint,
	IN _instanceguid uuid,
	IN _alternateid uuid,
	IN _element jsonb)
LANGUAGE plpgsql
AS $BODY$
BEGIN
    IF _element ->> 'IsRead' = 'false' THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '2')
        WHERE id = _instanceinternalid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = (_element ->> 'LastChanged')::TIMESTAMPTZ,
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_element ->> 'LastChanged'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_element ->> 'LastChangedBy'))
        WHERE id = _instanceinternalid;

    INSERT INTO storage.dataelements(instanceinternalid, instanceGuid, alternateid, element) VALUES (_instanceinternalid, _instanceGuid, _alternateid, jsonb_strip_nulls(_element));
END;
$BODY$;

CREATE OR REPLACE PROCEDURE storage.updatedataelement(_alternateid UUID, _element JSONB)
    LANGUAGE 'plpgsql'	
AS $BODY$
BEGIN
    IF _element ->> 'IsRead' = 'false' AND
        (SELECT COUNT(*) FROM storage.dataelements
            WHERE element -> 'IsRead' = 'true' AND instanceguid = (_element ->> 'InstanceGuid')::UUID AND alternateid <> _alternateid) = 0
    THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
        WHERE alternateid = (_element ->> 'InstanceGuid')::UUID AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = (_element ->> 'LastChanged')::TIMESTAMPTZ,
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(_element ->> 'LastChanged'))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_element ->> 'LastChangedBy'))            
        WHERE alternateid = (_element ->> 'InstanceGuid')::UUID;
    UPDATE storage.dataelements SET element = jsonb_strip_nulls(_element) WHERE alternateid = _alternateid;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.deletedataelement_v2(_alternateid UUID, _instanceGuid UUID, _lastChangedBy TEXT)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    IF (SELECT COUNT(*) FROM storage.dataelements WHERE element -> 'IsRead' = 'true' AND instanceguid = _instanceGuid) = 0 THEN
        UPDATE storage.instances
        SET instance = jsonb_set(instance, '{Status, ReadStatus}', '0')
        WHERE alternateid = _instanceGuid AND instance -> 'Status' ->> 'ReadStatus' = '1';
    END IF;

    UPDATE storage.instances
        SET lastchanged = NOW(),
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(NOW()))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb(_lastChangedBy))            
        WHERE alternateid = (SELECT instanceguid FROM storage.dataelements WHERE alternateid = _alternateid);

    DELETE FROM storage.dataelements WHERE alternateid = _alternateid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;

    RETURN _deleteCount;
END;
$BODY$;

CREATE OR REPLACE FUNCTION storage.deletedataelements(_instanceguid UUID)
    RETURNS INT
    LANGUAGE 'plpgsql'	
AS $BODY$
DECLARE
    _deleteCount INTEGER;
BEGIN
    UPDATE storage.instances
        SET lastchanged = NOW(),
            instance = instance
                || jsonb_set('{"LastChanged":""}', '{LastChanged}', to_jsonb(NOW()))
                || jsonb_set('{"LastChangedBy":""}', '{LastChangedBy}', to_jsonb('altinn'::TEXT))   
        WHERE alternateid = _instanceguid;

    DELETE FROM storage.dataelements d
        USING storage.instances i
        WHERE i.alternateid = d.instanceguid AND i.alternateid = _instanceguid;
    GET DIAGNOSTICS _deleteCount = ROW_COUNT;
    RETURN _deleteCount;
END;
$BODY$;