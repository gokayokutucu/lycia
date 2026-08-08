CREATE TABLE dbo.LyciaSagaData (
    SagaId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ApplicationId NVARCHAR(200) NULL,
    SagaDataType NVARCHAR(500) NOT NULL,
    DataJson NVARCHAR(MAX) NOT NULL,
    Version BIGINT NOT NULL CONSTRAINT DF_LyciaSagaData_Version DEFAULT (0),
    IsCompleted BIT NOT NULL CONSTRAINT DF_LyciaSagaData_IsCompleted DEFAULT (0),
    CompletedAtUtc DATETIME2(3) NULL,
    FailedAtUtc DATETIME2(3) NULL,
    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaSagaData_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
);

CREATE TABLE dbo.LyciaSagaSteps (
    SagaId UNIQUEIDENTIFIER NOT NULL,
    StepType NVARCHAR(500) NOT NULL,
    HandlerType NVARCHAR(500) NOT NULL,
    MessageId UNIQUEIDENTIFIER NOT NULL,
    ParentMessageId UNIQUEIDENTIFIER NULL,
    Status INT NOT NULL,
    MessageTypeName NVARCHAR(500) NOT NULL,
    ApplicationId NVARCHAR(200) NULL,
    MessagePayload NVARCHAR(MAX) NOT NULL,
    FailureInfoJson NVARCHAR(MAX) NULL,
    RecordedAtUtc DATETIME2(3) NOT NULL,
    CONSTRAINT PK_LyciaSagaSteps PRIMARY KEY (SagaId, StepType, HandlerType, MessageId)
);

CREATE INDEX IX_LyciaSagaSteps_SagaId_MessageId ON dbo.LyciaSagaSteps (SagaId, MessageId);
