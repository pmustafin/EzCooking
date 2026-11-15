ALTER TABLE recipes ADD COLUMN IF NOT EXISTS dismissed boolean NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS recipes_collection_order ON recipes (created_at DESC, id DESC) WHERE status = 'complete';
CREATE INDEX IF NOT EXISTS recipes_processing_order ON recipes (created_at DESC, id DESC) WHERE status <> 'complete';
