ALTER TABLE recipes ADD COLUMN IF NOT EXISTS audio_consent boolean NOT NULL DEFAULT false;
