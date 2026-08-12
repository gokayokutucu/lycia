// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using Lycia.Persistence.Relational.Internal.Migrations;
using Microsoft.Data.SqlClient;
namespace Lycia.Persistence.SqlServer;
internal static class SqlServerJournalSchemaMigrator
{
 public static Task RunAsync(SqlServerSagaStoreOptions options,CancellationToken token=default)=>RelationalMigrationRunner.RunAsync(
  ()=>new SqlConnection(options.ConnectionString),[new RelationalMigrationScript("004_SagaJournal",SqlServerSchemaMigrator.ReadEmbeddedScript("004_SagaJournal.sql",options.SchemaName))],
  SqlServerSchemaMigrator.CreateDialect(options.SchemaName),options.SchemaManagement,token);
}
