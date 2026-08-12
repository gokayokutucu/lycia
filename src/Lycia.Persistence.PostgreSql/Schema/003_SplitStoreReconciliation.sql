CREATE TABLE lycia_saga_reconciliation (
    transition_id uuid PRIMARY KEY,
    saga_id uuid NOT NULL REFERENCES lycia_saga_data(saga_id),
    message_id uuid,
    expected_version bigint NOT NULL,
    target_version bigint NOT NULL,
    saga_data_type varchar(1000) NOT NULL,
    payload jsonb NOT NULL,
    status integer NOT NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    worker_id varchar(300),
    failure_code varchar(300),
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    claimed_at_utc timestamptz,
    last_attempt_at_utc timestamptz,
    next_attempt_at_utc timestamptz,
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_lycia_saga_reconciliation_version UNIQUE (saga_id, target_version)
);

CREATE INDEX ix_lycia_saga_reconciliation_claim
    ON lycia_saga_reconciliation (status, next_attempt_at_utc, created_at_utc);
CREATE INDEX ix_lycia_saga_reconciliation_saga_version
    ON lycia_saga_reconciliation (saga_id, target_version DESC);
