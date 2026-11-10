CREATE OR REPLACE FUNCTION storage.get_studio_open_task_counts(_org TEXT)
    RETURNS TABLE (appid TEXT, taskid TEXT, open_instances BIGINT)
    LANGUAGE 'plpgsql'

AS $BODY$
BEGIN
    -- Hard deleted instances keep their taskid until the cleanup job removes them, so they are filtered out.
    -- The index instances_org_appid_taskid_created_open narrows the scan to the open rows.
    RETURN QUERY
        SELECT i.appid, i.taskid, count(*)
        FROM storage.instances i
        WHERE i.org = _org
            AND i.taskid IS NOT NULL
            AND NOT COALESCE((i.instance -> 'Status' ->> 'IsHardDeleted')::BOOLEAN, FALSE)
        GROUP BY i.appid, i.taskid
        ORDER BY i.appid, i.taskid;
END;
$BODY$;
