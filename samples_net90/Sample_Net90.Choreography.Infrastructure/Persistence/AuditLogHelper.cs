using Microsoft.EntityFrameworkCore;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Persistence;

public static class AuditLogHelper
{
    public static void CreateAuditInfrastructure(this DbContext context)
    {
        var baseEntityType = typeof(BaseEntity);
        var entityTypes = context.Model.GetEntityTypes()
            .Where(t => baseEntityType.IsAssignableFrom(t.ClrType) && !t.ClrType.IsAbstract)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var tableName = entityType.GetTableName();
            var auditTableName = $"{tableName}_Audit";
            var primaryKey = entityType.FindPrimaryKey()?.Properties.FirstOrDefault()?.Name ?? "Id";

            var baseEntityProperties = baseEntityType.GetProperties();

            // Drop audit columns from original table
            foreach (var prop in baseEntityProperties)
            {
                var columnName = prop.Name;
                var dropColumnSql = $"""
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_NAME = '{tableName}' AND COLUMN_NAME = '{columnName}'
                )
                BEGIN
                    ALTER TABLE [{tableName}] DROP COLUMN [{columnName}];
                END
                """;

                context.Database.ExecuteSqlRaw(dropColumnSql);
            }

            // Build audit columns dynamically from BaseEntity (except AuditId, which is treated separately)
            var auditColumns = baseEntityProperties
                .Where(p => p.Name is not nameof(BaseEntity.AuditId))
                .Select(p =>
                {
                    var columnName = $"[{p.Name}]";
                    var columnType = p.PropertyType switch
                    {
                        Type t when t == typeof(Guid) => "UNIQUEIDENTIFIER",
                        Type t when t == typeof(DateTime) => "DATETIME2",
                        Type t when t == typeof(int) => "INT",
                        Type t when t == typeof(bool) => "BIT",
                        Type t when t == typeof(string) => "NVARCHAR(MAX)",
                        _ => "NVARCHAR(MAX)"
                    };
                    return $"{columnName} {columnType}";
                })
                .ToList();

            var auditPk = baseEntityProperties.FirstOrDefault(p => p.Name is nameof(BaseEntity.AuditId))?.Name ?? "AuditId";

            auditColumns.Insert(0, $"[{auditPk}] UNIQUEIDENTIFIER");
            auditColumns.Insert(1, $"[{primaryKey}] UNIQUEIDENTIFIER");

            var columnList = string.Join(",\n    ", auditColumns);

            var createAuditTableSql = $"""
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='{auditTableName}' AND xtype='U')
                BEGIN
                    CREATE TABLE [{auditTableName}] (
                        {columnList}
                    );
                END
                """;

            context.Database.ExecuteSqlRaw(createAuditTableSql);

            var createTriggerSql = $@"
                IF OBJECT_ID ('TR_{tableName}_Audit', 'TR') IS NULL
                BEGIN
                    EXEC('
                        CREATE TRIGGER [TR_{tableName}_Audit]
                        ON [{tableName}]
                        AFTER INSERT, UPDATE, DELETE
                        AS
                        BEGIN
                            SET NOCOUNT ON;

                            -- INSERT and UPDATE
                            IF EXISTS (SELECT * FROM INSERTED)
                            BEGIN
                                INSERT INTO [{auditTableName}] (
                                    [AuditId], [{primaryKey}], [By], [At], [Action], [Detail]
                                )
                                SELECT 
                                    NEWID(),
                                    i.[{primaryKey}],
                                    SYSTEM_USER,
                                    GETDATE(),
                                    CASE 
                                        WHEN EXISTS (SELECT * FROM DELETED) THEN 2
                                        ELSE 0
                                    END,
                                    ''{{""old"":'' + ISNULL((SELECT * FROM DELETED WHERE [{primaryKey}] = i.[{primaryKey}] FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER), ''null'') 
                                    + '',''""new"":'' + ISNULL((SELECT * FROM INSERTED WHERE [{primaryKey}] = i.[{primaryKey}] FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER), ''null'') 
                                    + ''}}''
                                FROM INSERTED i
                            END

                            -- DELETE
                            IF EXISTS (SELECT * FROM DELETED) AND NOT EXISTS (SELECT * FROM INSERTED)
                            BEGIN
                                INSERT INTO [{auditTableName}] (
                                    [AuditId], [{primaryKey}], [By], [At], [Action], [Detail]
                                )
                                SELECT 
                                    NEWID(),
                                    d.[{primaryKey}],
                                    SYSTEM_USER,
                                    GETDATE(),
                                    1,
                                    ''{{""old"":'' + (SELECT * FROM DELETED WHERE [{primaryKey}] = d.[{primaryKey}] FOR JSON AUTO, WITHOUT_ARRAY_WRAPPER) + ''}}''
                                FROM DELETED d
                            END
                        END
                    ')
                END
                ";

            context.Database.ExecuteSqlRaw(createTriggerSql);
        }
    }
}
