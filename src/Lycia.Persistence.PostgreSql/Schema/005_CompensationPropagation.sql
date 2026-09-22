CREATE TABLE lycia_compensation_propagation (
    saga_id uuid NOT NULL,
    child_message_id uuid NOT NULL,
    parent_message_id uuid NOT NULL,
    status integer NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    owner varchar(200),
    failure_info_json jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (saga_id, child_message_id)
);

CREATE INDEX ix_lycia_compensation_propagation_claimable ON lycia_compensation_propagation (status, updated_at_utc);
