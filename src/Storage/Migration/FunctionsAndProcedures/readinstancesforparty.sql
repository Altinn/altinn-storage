CREATE OR REPLACE FUNCTION storage.readinstancesforparty_v1(
    _instanceOwner_partyId BIGINT,
    _created_idx TIMESTAMPTZ DEFAULT NULL,
    _continue_idx BIGINT DEFAULT -1,
    _size INTEGER DEFAULT 100
    )
    RETURNS TABLE (id BIGINT, instance JSONB, created TIMESTAMPTZ, instanceversion INT, processstateversion INT, element JSONB, currentblobversion UUID)
    LANGUAGE 'plpgsql'

AS $BODY$
BEGIN
    RETURN QUERY
    WITH filteredInstances AS
    (
        SELECT i.id, i.instance, i.created, i.instance_version, i.process_state_version FROM storage.instances i
        WHERE i.partyId = _instanceOwner_partyId
            AND (i.instance -> 'Status' -> 'IsHardDeleted')::BOOLEAN IS NOT TRUE
            AND (_continue_idx <= 0 OR (i.created, i.id) > (_created_idx, _continue_idx))
        ORDER BY i.created, i.id
        FETCH FIRST _size ROWS ONLY
    )
        SELECT filteredInstances.id, filteredInstances.instance, filteredInstances.created, filteredInstances.instance_version, filteredInstances.process_state_version, d.element, d.currentblobversion FROM filteredInstances
            LEFT JOIN storage.dataelements d ON filteredInstances.id = d.instanceInternalId
                AND (d.element -> 'DeleteStatus' -> 'IsHardDeleted')::BOOLEAN IS NOT TRUE
        ORDER BY filteredInstances.created, filteredInstances.id, d.id;
END;
$BODY$;
