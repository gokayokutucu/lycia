CREATE TABLE lycia_saga_data (
    saga_id uuid PRIMARY KEY,
    application_id varchar(200),
    saga_data_type varchar(500) NOT NULL,
    data_json jsonb NOT NULL,
    version bigint NOT NULL DEFAULT 0,
    is_completed boolean NOT NULL DEFAULT false,
    completed_at_utc timestamptz,
    failed_at_utc timestamptz,
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE lycia_saga_steps (
    saga_id uuid NOT NULL REFERENCES lycia_saga_data(saga_id),
    step_type varchar(500) NOT NULL,
    handler_type varchar(500) NOT NULL,
    message_id uuid NOT NULL,
    parent_message_id uuid,
    status integer NOT NULL,
    message_type_name varchar(500) NOT NULL,
    application_id varchar(200),
    message_payload jsonb NOT NULL,
    failure_info_json jsonb,
    recorded_at_utc timestamptz NOT NULL,
    PRIMARY KEY (saga_id, step_type, handler_type, message_id)
);

CREATE INDEX ix_lycia_saga_steps_saga_message ON lycia_saga_steps (saga_id, message_id);
