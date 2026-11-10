-- Altinn 2 is offline, so nothing migrates instances, XSLs, code lists or images from it any more.
-- The read functions for A2 XSLs, code lists and images stay because ContentOnDemand uses them.
DROP PROCEDURE IF EXISTS storage.inserta1migrationstate(BIGINT);
DROP PROCEDURE IF EXISTS storage.inserta2migrationstate(BIGINT);
DROP PROCEDURE IF EXISTS storage.updatea1migrationstatestarted(BIGINT, UUID);
DROP PROCEDURE IF EXISTS storage.updatea2migrationstatestarted(BIGINT, UUID);
DROP PROCEDURE IF EXISTS storage.updatemigrationstatecompleted(UUID);
DROP PROCEDURE IF EXISTS storage.deletemigrationstate(UUID);
DROP FUNCTION IF EXISTS storage.reada1migrationstate(BIGINT);
DROP FUNCTION IF EXISTS storage.reada2migrationstate(BIGINT);
DROP PROCEDURE IF EXISTS storage.inserta2codelist(TEXT, TEXT, INT, TEXT);
DROP PROCEDURE IF EXISTS storage.inserta2image(TEXT, BYTEA);
DROP PROCEDURE IF EXISTS storage.inserta2xsl(TEXT, TEXT, INT, TEXT, INT, TEXT, INT, BOOL);
DROP PROCEDURE IF EXISTS storage.inserta2xsl(TEXT, TEXT, INT, TEXT, INT, TEXT);
DROP PROCEDURE IF EXISTS storage.inserta2xsl(TEXT, TEXT, INT, TEXT, INT, TEXT, INT);
DROP PROCEDURE IF EXISTS storage.deletea2migrationstate(UUID);
DROP PROCEDURE IF EXISTS storage.updatea2migrationstatecompleted(UUID);
