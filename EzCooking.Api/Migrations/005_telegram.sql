CREATE TABLE telegram_bot_state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    bot_id bigint NOT NULL,
    user_id bigint,
    next_update bigint NOT NULL DEFAULT 0,
    last_update_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE telegram_submissions (
    id uuid PRIMARY KEY,
    source_url text NOT NULL,
    telegram_message_id bigint NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    state text NOT NULL DEFAULT 'pending',
    recipe_id uuid REFERENCES recipes(id) ON DELETE SET NULL,
    last_notified_status text,
    UNIQUE (telegram_message_id)
);
