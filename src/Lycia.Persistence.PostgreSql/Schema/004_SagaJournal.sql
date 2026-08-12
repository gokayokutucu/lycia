CREATE TABLE lycia_saga_journal (
    journal_entry_id uuid NOT NULL,
    transition_id uuid PRIMARY KEY,
    saga_id uuid NOT NULL,
    sequence_number bigint NOT NULL,
    previous_version bigint NOT NULL,
    target_version bigint NOT NULL,
    message_id uuid,
    request_id uuid,
    correlation_id uuid,
    causation_id uuid,
    parent_message_id uuid,
    application_id varchar(200),
    handler_type varchar(1000),
    message_type varchar(1000),
    message_schema_version integer NOT NULL DEFAULT 1,
    journal_schema_version integer NOT NULL DEFAULT 1,
    transition_type integer NOT NULL,
    saga_data_type_name varchar(1000) NOT NULL,
    saga_data_payload jsonb NOT NULL,
    steps_snapshot_payload jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_lycia_saga_journal_version UNIQUE (saga_id, target_version)
);

CREATE INDEX ix_lycia_saga_journal_saga_sequence
    ON lycia_saga_journal (saga_id, sequence_number);
