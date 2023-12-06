 #!/bin/bash
export PGPASSWORD=Password

# set up platform_storage role
psql -h localhost -p 5432 -U platform_storage_admin -d storagedb \
-c "DO \$\$
    BEGIN CREATE ROLE platform_storage WITH LOGIN  PASSWORD 'Password';
    EXCEPTION WHEN duplicate_object THEN RAISE NOTICE '%, skipping', SQLERRM USING ERRCODE = SQLSTATE;
    END \$\$;"