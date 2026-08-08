CREATE TABLE lycia_inbox (
    message_id uuid NOT NULL,
    handler_type varchar(500) NOT NULL,
    status integer NOT NULL,
    failure_info_json jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (message_id, handler_type)
);

CREATE TABLE lycia_outbox (
    message_id uuid PRIMARY KEY,
    message_type_name varchar(500) NOT NULL,
    payload jsonb NOT NULL,
    application_id varchar(200),
    saga_id uuid,
    status integer NOT NULL,
    retry_count integer NOT NULL DEFAULT 0,
    failure_info_json jsonb,
    created_at_utc timestamptz NOT NULL DEFAULT now(),
    updated_at_utc timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_lycia_outbox_status_created_at ON lycia_outbox (status, created_at_utc);
