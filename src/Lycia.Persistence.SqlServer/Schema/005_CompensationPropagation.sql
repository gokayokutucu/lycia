CREATE TABLE dbo.LyciaCompensationPropagation (
    SagaId UNIQUEIDENTIFIER NOT NULL,
    ChildMessageId UNIQUEIDENTIFIER NOT NULL,
    ParentMessageId UNIQUEIDENTIFIER NOT NULL,
    Status INT NOT NULL,
    AttemptCount INT NOT NULL CONSTRAINT DF_LyciaCompensationPropagation_AttemptCount DEFAULT (0),
    [Owner] NVARCHAR(200) NULL,
    FailureInfoJson NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaCompensationPropagation_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
    UpdatedAtUtc DATETIME2(3) NOT NULL CONSTRAINT DF_LyciaCompensationPropagation_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_LyciaCompensationPropagation PRIMARY KEY (SagaId, ChildMessageId)
);

CREATE INDEX IX_LyciaCompensationPropagation_Claimable ON dbo.LyciaCompensationPropagation (Status, UpdatedAtUtc);
