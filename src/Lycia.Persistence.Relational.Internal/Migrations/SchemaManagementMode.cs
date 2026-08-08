// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Persistence.Relational.Internal.Migrations;

/// <summary>
/// Controls how a relational SagaStore provider manages its own schema at startup.
/// </summary>
public enum SchemaManagementMode
{
    /// <summary>Applies any migration script that has not yet been recorded as applied.</summary>
    ApplyMigrations,

    /// <summary>Does not apply migrations; throws if the schema is missing a required migration.</summary>
    ValidateOnly,

    /// <summary>Skips schema management entirely. The caller is responsible for provisioning the schema.</summary>
    Disabled
}
