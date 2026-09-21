CREATE TABLE dbo.LyciaSagaReconciliation (
    TransitionId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    SagaId UNIQUEIDENTIFIER NOT NULL,
    MessageId UNIQUEIDENTIFIER NULL,
    ExpectedVersion BIGINT NOT NULL,
    TargetVersion BIGINT NOT NULL,
    SagaDataType NVARCHAR(1000) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Status INT NOT NULL,
    AttemptCount INT NOT NULL CONSTRAINT DF_LyciaSagaReconciliation_AttemptCount DEFAULT (0),
    WorkerId NVARCHAR(300) NULL,
    FailureCode NVARCHAR(300) NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaSagaReconciliation_Created DEFAULT (SYSUTCDATETIME()),
    ClaimedAtUtc DATETIME2(3) NULL,
    LastAttemptAtUtc DATETIME2(3) NULL,
    NextAttemptAtUtc DATETIME2(3) NULL,
    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaSagaReconciliation_Updated DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT UQ_LyciaSagaReconciliation_SagaVersion UNIQUE (SagaId, TargetVersion)
);
CREATE INDEX IX_LyciaSagaReconciliation_Claim ON dbo.LyciaSagaReconciliation (Status, NextAttemptAtUtc, CreatedAtUtc);
CREATE INDEX IX_LyciaSagaReconciliation_SagaVersion ON dbo.LyciaSagaReconciliation (SagaId, TargetVersion DESC);
