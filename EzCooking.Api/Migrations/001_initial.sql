CREATE TABLE IF NOT EXISTS recipes (
    id uuid PRIMARY KEY,
    source_url text,
    status text NOT NULL,
    error text,
    document jsonb,
    version integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now()
);
