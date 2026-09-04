CREATE OR REPLACE FUNCTION storage.mergeinstanceupdate(
    _instance JSONB,
    _instanceupdate JSONB)
    RETURNS JSONB
    LANGUAGE SQL IMMUTABLE
AS $BODY$
    SELECT _instance || updateparts.toplevelsimpleprops
        || CASE
            WHEN updateparts.datavalues IS NOT NULL THEN
                jsonb_build_object(
                    'DataValues',
                    jsonb_strip_nulls(
                        COALESCE(_instance -> 'DataValues', '{}'::JSONB)
                            || updateparts.datavalues
                    )
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.presentationtexts IS NOT NULL THEN
                jsonb_build_object(
                    'PresentationTexts',
                    jsonb_strip_nulls(
                        COALESCE(_instance -> 'PresentationTexts', '{}'::JSONB)
                            || updateparts.presentationtexts
                    )
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.completeconfirmations IS NOT NULL THEN
                jsonb_build_object(
                    'CompleteConfirmations',
                    COALESCE(_instance -> 'CompleteConfirmations', '[]'::JSONB)
                        || (
                            SELECT COALESCE(jsonb_agg(incoming.value), '[]'::JSONB)
                            FROM jsonb_array_elements(updateparts.completeconfirmations) AS incoming
                            WHERE NOT EXISTS (
                                SELECT 1
                                FROM jsonb_array_elements(
                                    COALESCE(_instance -> 'CompleteConfirmations', '[]'::JSONB)
                                ) AS existing
                                WHERE existing.value ->> 'StakeholderId'
                                    = incoming.value ->> 'StakeholderId'
                            )
                        )
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.status IS NOT NULL OR updateparts.substatus IS NOT NULL THEN
                jsonb_build_object(
                    'Status',
                    CASE
                        WHEN updateparts.substatus IS NOT NULL THEN
                            jsonb_set(
                                COALESCE(_instance -> 'Status', '{}'::JSONB)
                                    || COALESCE(updateparts.status, '{}'::JSONB),
                                '{Substatus}',
                                jsonb_strip_nulls(updateparts.substatus)
                            )
                        ELSE
                            COALESCE(_instance -> 'Status', '{}'::JSONB) || updateparts.status
                    END
                )
            ELSE
                '{}'::JSONB
        END
        || CASE
            WHEN updateparts.process IS NOT NULL THEN
                jsonb_build_object('Process', jsonb_strip_nulls(updateparts.process))
            ELSE
                '{}'::JSONB
        END
    FROM (
        SELECT
            COALESCE(NULLIF(_instanceupdate -> 'toplevelsimpleprops', 'null'::JSONB), '{}'::JSONB)
                - 'DataValues'
                - 'PresentationTexts'
                - 'CompleteConfirmations'
                - 'Status'
                - 'Process' AS toplevelsimpleprops,
            NULLIF(_instanceupdate -> 'datavalues', 'null'::JSONB) AS datavalues,
            NULLIF(_instanceupdate -> 'presentationtexts', 'null'::JSONB) AS presentationtexts,
            NULLIF(_instanceupdate -> 'completeconfirmations', 'null'::JSONB) AS completeconfirmations,
            NULLIF(_instanceupdate -> 'status', 'null'::JSONB) AS status,
            NULLIF(_instanceupdate -> 'substatus', 'null'::JSONB) AS substatus,
            NULLIF(_instanceupdate -> 'process', 'null'::JSONB) AS process
    ) updateparts;
$BODY$;
