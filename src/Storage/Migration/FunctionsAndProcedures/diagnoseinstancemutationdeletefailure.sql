CREATE OR REPLACE PROCEDURE storage.diagnoseinstancemutationdeletefailure(
    _deleteelements JSONB,
    _instanceguid UUID,
    _currentinstanceversion INT,
    _currentprocessstateversion INT,
    _deleteddataelementids UUID[])
LANGUAGE plpgsql
AS $BODY$
DECLARE
    _faileddataelementid UUID;
    _errorcode TEXT;
BEGIN
    WITH targetdataelements AS (
        SELECT *
        FROM (
            SELECT
                deleteelement.ordinality,
                (deleteelement.value ->> 'elementId')::UUID AS dataelementid,
                COALESCE((deleteelement.value ->> 'ignoreLock')::BOOL, false) AS ignorelock
            FROM jsonb_array_elements(_deleteelements) WITH ORDINALITY deleteelement(value, ordinality)
        ) deleteelements
        WHERE NOT COALESCE(
            deleteelements.dataelementid = ANY(_deleteddataelementids),
            false)
    ),
    targetdataelementstates AS (
        SELECT
            targetdataelements.ordinality,
            targetdataelements.dataelementid,
            targetdataelements.ignorelock,
            dataelement.alternateid IS NOT NULL AS targetexists,
            COALESCE((dataelement.element ->> 'Locked')::BOOLEAN, false) AS elementislocked
        FROM targetdataelements
        LEFT JOIN storage.dataelements dataelement
            ON dataelement.instanceguid = _instanceguid
            AND dataelement.alternateid = targetdataelements.dataelementid
    ),
    validationerrors AS (
        SELECT
            targetdataelementstates.ordinality,
            1 AS priority,
            targetdataelementstates.dataelementid,
            'data_element_not_found'::TEXT AS errorcode
        FROM targetdataelementstates
        WHERE NOT targetdataelementstates.targetexists

        UNION ALL

        SELECT
            targetdataelementstates.ordinality,
            2 AS priority,
            targetdataelementstates.dataelementid,
            'locked'::TEXT AS errorcode
        FROM targetdataelementstates
        WHERE targetdataelementstates.targetexists
            AND NOT targetdataelementstates.ignorelock
            AND targetdataelementstates.elementislocked
    )
    SELECT validationerrors.dataelementid, validationerrors.errorcode
    INTO _faileddataelementid, _errorcode
    FROM validationerrors
    ORDER BY validationerrors.priority, validationerrors.ordinality
    LIMIT 1;

    IF _errorcode IS NOT NULL
    THEN
        CALL storage.raiseinstancemutationerror(
            _errorcode,
            _currentinstanceversion,
            _currentprocessstateversion,
            _faileddataelementid);
        RETURN;
    END IF;

    RAISE EXCEPTION 'Apply instance mutation delete affected fewer data elements than expected, but no failing item could be identified.';
END;
$BODY$;
