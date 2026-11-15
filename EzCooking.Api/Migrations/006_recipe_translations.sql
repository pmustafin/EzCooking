CREATE TABLE recipe_translations (
    recipe_id uuid PRIMARY KEY REFERENCES recipes(id) ON DELETE CASCADE,
    recipe_version integer NOT NULL,
    status text NOT NULL,
    document jsonb,
    error text,
    updated_at timestamptz NOT NULL DEFAULT now()
);
