CREATE TABLE dbo.LyciaInbox (
    MessageId UNIQUEIDENTIFIER NOT NULL,
    HandlerType NVARCHAR(500) NOT NULL,
    Status INT NOT NULL,
    FailureInfoJson NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaInbox_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaInbox_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_LyciaInbox PRIMARY KEY (MessageId, HandlerType)
);

CREATE TABLE dbo.LyciaOutbox (
    MessageId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    MessageTypeName NVARCHAR(500) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    ApplicationId NVARCHAR(200) NULL,
    SagaId UNIQUEIDENTIFIER NULL,
    Status INT NOT NULL,
    RetryCount INT NOT NULL CONSTRAINT DF_LyciaOutbox_RetryCount DEFAULT (0),
    FailureInfoJson NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaOutbox_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaOutbox_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
);
CREATE INDEX IX_LyciaOutbox_Status_CreatedAtUtc ON dbo.LyciaOutbox (Status, CreatedAtUtc);
