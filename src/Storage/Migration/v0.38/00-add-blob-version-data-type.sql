-- Attachment records the data type so retained blob versions can be authorized after element deletion.
ALTER TABLE storage.dataelementblobversions
ADD COLUMN IF NOT EXISTS datatype TEXT NULL;
