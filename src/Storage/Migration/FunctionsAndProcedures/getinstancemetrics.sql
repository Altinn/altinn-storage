CREATE OR REPLACE FUNCTION storage.get_instance_metrics(
    day_input integer,
    month_input integer,
    year_input integer)
    RETURNS TABLE (org TEXT, appid TEXT, completed_instances bigint)
    LANGUAGE 'plpgsql'

AS $BODY$
DECLARE
    start_date TIMESTAMPTZ;
BEGIN
    start_date = MAKE_TIMESTAMPTZ(year_input, month_input, day_input, 0, 0, 0, 'UTC');
    RETURN QUERY
        SELECT
            i.org,
            i.appid,
            count(i.id) AS CompletedInstances
        FROM storage.instances i
        WHERE (i.instance -> 'Status' ->> 'IsArchived')::BOOL = TRUE
            AND i.instance -> 'Status' ? 'Archived'
            AND NULLIF(i.instance -> 'Status' ->> 'Archived', '') IS NOT NULL
		    AND (i.instance -> 'Status' ->> 'Archived')::TIMESTAMPTZ >= start_date
		    AND (i.instance -> 'Status' ->> 'Archived')::TIMESTAMPTZ < start_date + INTERVAL '1 day'
		    AND i.altinnmainversion = 3
        GROUP BY i.org, i.appid;
END;
$BODY$;
