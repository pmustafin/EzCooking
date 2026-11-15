ALTER TABLE recipes ADD COLUMN IF NOT EXISTS upload_sha256 text;
CREATE INDEX IF NOT EXISTS recipes_source_url_idx ON recipes (source_url);
CREATE INDEX IF NOT EXISTS recipes_upload_sha256_idx ON recipes (upload_sha256);
