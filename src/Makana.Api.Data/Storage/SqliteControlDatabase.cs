using Dapper;
using Makana.Journaling.Checkpointing;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

public sealed class SqliteControlDatabase(string connectionString)
{
    private readonly string? _dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

    public string ConnectionString { get; } = connectionString;

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        SqliteConnectionInitializer.Configure(connection);
        return connection;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var tx = connection.BeginImmediateTransaction();

        ReadOnlySpan<string> commands = [
            @"CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                github_login TEXT UNIQUE NOT NULL,
                display_name TEXT NOT NULL,
                email TEXT,
                avatar_url TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS orgs (
                id TEXT PRIMARY KEY,
                login TEXT UNIQUE NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS org_memberships (
                org_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                role TEXT NOT NULL DEFAULT 'member',
                PRIMARY KEY (org_id, user_id)
            );",
            @"CREATE TABLE IF NOT EXISTS org_invitations (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                email TEXT NOT NULL,
                role TEXT NOT NULL,
                created_at TEXT NOT NULL,
                claimed_at TEXT,
                revoked_at TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS org_sso_configs (
                org_id TEXT PRIMARY KEY,
                protocol TEXT NOT NULL DEFAULT 'oidc',
                mode TEXT NOT NULL,
                connector_id TEXT,
                display_name TEXT,
                scim_bearer_token TEXT,
                oidc_issuer TEXT,
                oidc_client_id TEXT,
                oidc_client_secret TEXT,
                saml_sso_url TEXT,
                saml_ca_certificate_pem TEXT,
                saml_username_attribute TEXT,
                saml_email_attribute TEXT,
                saml_groups_attribute TEXT,
                saml_entity_issuer TEXT,
                saml_sso_issuer TEXT,
                saml_groups_delimiter TEXT,
                saml_name_id_policy_format TEXT,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS org_verified_domains (
                org_id TEXT NOT NULL,
                domain TEXT NOT NULL,
                PRIMARY KEY (org_id, domain),
                UNIQUE(domain)
            );",
            @"CREATE TABLE IF NOT EXISTS org_scim_memberships (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                email TEXT NOT NULL,
                display_name TEXT,
                role TEXT NOT NULL,
                external_id TEXT NOT NULL,
                active INTEGER NOT NULL DEFAULT 1,
                claimed_user_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(org_id, external_id)
            );",
            @"CREATE TABLE IF NOT EXISTS api_tokens (
                token TEXT PRIMARY KEY,
                token_id TEXT,
                token_type TEXT,
                principal_type TEXT,
                principal_id TEXT,
                user_id TEXT NOT NULL,
                org_id TEXT,
                team_id TEXT,
                display_name TEXT,
                description TEXT,
                issued_at TEXT,
                expires_at TEXT,
                revoked_at TEXT,
                last_used_at TEXT,
                issued_via TEXT,
                linked_identity_id TEXT,
                created_by_user_id TEXT,
                scope_admin INTEGER NOT NULL DEFAULT 0
            );",
            @"CREATE TABLE IF NOT EXISTS linked_identities (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                issuer TEXT NOT NULL,
                subject TEXT NOT NULL,
                preferred_username TEXT,
                email TEXT,
                email_verified INTEGER NOT NULL DEFAULT 0,
                last_seen_at TEXT NOT NULL,
                UNIQUE(issuer, subject)
            );",
            @"CREATE TABLE IF NOT EXISTS teams (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                name TEXT NOT NULL,
                display_name TEXT,
                UNIQUE(org_id, name)
            );",
            @"CREATE TABLE IF NOT EXISTS team_memberships (
                team_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                PRIMARY KEY (team_id, user_id)
            );",
            @"CREATE TABLE IF NOT EXISTS user_preferences (
                user_id TEXT PRIMARY KEY,
                theme TEXT,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                name TEXT NOT NULL,
                UNIQUE(org_id, name)
            );",
            @"CREATE TABLE IF NOT EXISTS stacks (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL,
                tags_json BLOB,
                config_json BLOB,
                shard_path TEXT,
                shard_state TEXT,
                shard_migrated_at TEXT,
                UNIQUE(org_id, project_id, name)
            ) STRICT;",
            @"CREATE TABLE IF NOT EXISTS update_locations (
                update_id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                created_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS deployment_run_locations (
                deployment_run_id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                created_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS registry_ops (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                payload_json BLOB,
                created_at TEXT NOT NULL,
                completed_at TEXT
            ) STRICT;",
            @"CREATE TABLE IF NOT EXISTS service_secrets (
                name TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS uploads (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL,
                content_type TEXT,
                content BLOB NOT NULL,
                created_at TEXT NOT NULL
            );",
            @"CREATE INDEX IF NOT EXISTS idx_stacks_shard_state
              ON stacks (shard_state);",
            @"CREATE UNIQUE INDEX IF NOT EXISTS idx_api_tokens_token_id
              ON api_tokens (token_id);",
            @"CREATE INDEX IF NOT EXISTS idx_api_tokens_expires_at
              ON api_tokens (expires_at);",
            @"CREATE INDEX IF NOT EXISTS idx_api_tokens_org_id
              ON api_tokens (org_id);",
            @"CREATE INDEX IF NOT EXISTS idx_api_tokens_team_id
              ON api_tokens (team_id);",
            @"CREATE INDEX IF NOT EXISTS idx_api_tokens_revoked_at
              ON api_tokens (revoked_at);",
            @"CREATE INDEX IF NOT EXISTS idx_linked_identities_user_id
              ON linked_identities (user_id);",
            @"CREATE INDEX IF NOT EXISTS idx_org_invitations_org_id
              ON org_invitations (org_id);",
            @"CREATE INDEX IF NOT EXISTS idx_org_invitations_email
              ON org_invitations (email);",
            @"CREATE INDEX IF NOT EXISTS idx_org_scim_memberships_org_id
              ON org_scim_memberships (org_id);",
            @"CREATE INDEX IF NOT EXISTS idx_org_scim_memberships_email
              ON org_scim_memberships (email);",
            @"CREATE INDEX IF NOT EXISTS idx_teams_org_id
              ON teams (org_id);",
            @"CREATE INDEX IF NOT EXISTS idx_team_memberships_user_id
              ON team_memberships (user_id);",
            @"CREATE INDEX IF NOT EXISTS idx_uploads_created_at
              ON uploads (created_at);",
            @"CREATE INDEX IF NOT EXISTS idx_registry_ops_completed_created_at
              ON registry_ops (completed_at, created_at);",
            @"CREATE INDEX IF NOT EXISTS idx_update_locations_stack_id
              ON update_locations (stack_id);",
            @"CREATE INDEX IF NOT EXISTS idx_deployment_run_locations_stack_id
              ON deployment_run_locations (stack_id);",
        ];

        foreach (var command in commands)
        {
            connection.Execute(command, transaction: tx);
        }

        EnsureColumnExists(connection, tx, "orgs", "display_name", "TEXT");
        EnsureColumnExists(connection, tx, "org_memberships", "role", "TEXT NOT NULL DEFAULT 'member'");
        EnsureColumnExists(connection, tx, "org_sso_configs", "protocol", "TEXT NOT NULL DEFAULT 'oidc'");
        EnsureColumnExists(connection, tx, "org_sso_configs", "oidc_issuer", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "oidc_client_id", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "oidc_client_secret", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_sso_url", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_ca_certificate_pem", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_username_attribute", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_email_attribute", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_groups_attribute", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_entity_issuer", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_sso_issuer", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_groups_delimiter", "TEXT");
        EnsureColumnExists(connection, tx, "org_sso_configs", "saml_name_id_policy_format", "TEXT");
        EnsureColumnExists(connection, tx, "api_tokens", "revoked_at", "TEXT");
        EnsureColumnExists(connection, tx, "api_tokens", "last_used_at", "TEXT");
        EnsureColumnExists(connection, tx, "api_tokens", "issued_via", "TEXT");
        EnsureColumnExists(connection, tx, "api_tokens", "linked_identity_id", "TEXT");
        EnsureColumnExists(connection, tx, "api_tokens", "created_by_user_id", "TEXT");
        EnsureColumnExists(connection, tx, "api_tokens", "scope_admin", "INTEGER NOT NULL DEFAULT 0");

        tx.Commit();
    }

    private static void EnsureColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = $"SELECT 1 FROM pragma_table_info('{tableName}') WHERE name = $column LIMIT 1;";
        existsCommand.Parameters.AddWithValue("$column", columnName);
        var exists = existsCommand.ExecuteScalar() is not null;
        if (exists)
        {
            return;
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.Transaction = transaction;
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alterCommand.ExecuteNonQuery();
    }
}
