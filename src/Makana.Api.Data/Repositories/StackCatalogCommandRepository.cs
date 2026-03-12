using System.Text.Json.Nodes;
using Dapper;

namespace Makana.Api.Storage;

public sealed class StackCatalogCommandRepository(SqliteControlDatabase database, StackShardDatabase stackShardDatabase)
{
    public string CreateStackByNamespaceId(string namespaceId, string project, string stack, string? tagsJson, string? configJson)
    {
        var projectId = EnsureProjectByNamespaceId(namespaceId, project);
        var stackId = Guid.NewGuid().ToString("N");

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        connection.Execute(
            """
            INSERT INTO stacks (id, org_id, project_id, name, tags_json, config_json)
            VALUES (@id, @namespaceId, @projectId, @name, @tags, @config);
            """,
            new
            {
                id = stackId,
                namespaceId,
                projectId,
                name = stack,
                tags = tagsJson,
                config = configJson
            },
            transaction);
        transaction.Commit();
        StorageRepositoryCommon.EnsureShardProvisioned(database, stackShardDatabase, stackId, namespaceId);
        return stackId;
    }

    public void SaveInitialDeploymentForStack(string stackId, int version, int schemaVersion, string deploymentJson)
    {
        var normalizedDeploymentJson = DeploymentSnapshotNormalizer.NormalizeForPersistence(deploymentJson);
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        connection.Execute(
            """
            INSERT OR REPLACE INTO deployments (id, stack_id, version, schema_version, deployment_json, created_at)
            VALUES (@id, @stackId, @version, @schemaVersion, @deployment, @created);
            """,
            new
            {
                id = Guid.NewGuid().ToString("N"),
                stackId,
                version,
                schemaVersion,
                deployment = normalizedDeploymentJson,
                created = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public void UpdateTags(string org, string project, string stack, string tagsJson)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            UPDATE stacks
            SET tags_json = @tags
            WHERE id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users u ON u.id = s.org_id
                WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            );
            """,
            new { tags = tagsJson, org, project, stack });
    }

    public void UpdateConfig(string org, string project, string stack, string configJson)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            UPDATE stacks
            SET config_json = @config
            WHERE id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users u ON u.id = s.org_id
                WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            );
            """,
            new { config = configJson, org, project, stack });
    }

    public void RenameStack(string org, string project, string stack, string newName, string? newProject, CancellationToken cancellationToken = default)
    {
        var targetProject = string.IsNullOrWhiteSpace(newProject) ? project : newProject!;
        var projectId = EnsureProject(org, targetProject);

        using var connection = database.OpenConnection();
        using var tx = connection.BeginImmediateTransaction();
        var stackId = connection.QuerySingleOrDefault<string>(
            """
            SELECT s.id
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            LIMIT 1;
            """,
            new { org, project, stack },
            tx);
        if (string.IsNullOrWhiteSpace(stackId))
        {
            tx.Rollback();
            return;
        }

        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        connection.Execute(
            """
            UPDATE stacks
            SET name = @newName, project_id = @projectId
            WHERE id = @stackId;
            """,
            new { newName, projectId, stackId },
            tx);
        tx.Commit();

        using var shardConnection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        using var shardTx = shardConnection.BeginImmediateTransaction();
        var shardDeployments = shardConnection.Query<DeploymentDocumentRow>(
            """
            SELECT id AS Id, deployment_json AS DeploymentJson
            FROM deployments
            WHERE stack_id = @stackId;
            """,
            new { stackId },
            shardTx);
        var shardCheckpoints = shardConnection.Query<DeploymentDocumentRow>(
            """
            SELECT c.id AS Id, c.deployment_json AS DeploymentJson
            FROM update_checkpoints c
            JOIN updates u ON u.id = c.update_id
            WHERE u.stack_id = @stackId;
            """,
            new { stackId },
            shardTx);

        foreach (var deployment in shardDeployments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rewritten = RewriteDeploymentForRenamedStack(deployment.DeploymentJson, project, stack, targetProject, newName);
            shardConnection.Execute(
                "UPDATE deployments SET deployment_json = @deploymentJson WHERE id = @id;",
                new { deploymentJson = rewritten, id = deployment.Id },
                shardTx);
        }

        foreach (var checkpoint in shardCheckpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rewritten = RewriteDeploymentForRenamedStack(checkpoint.DeploymentJson, project, stack, targetProject, newName);
            shardConnection.Execute(
                "UPDATE update_checkpoints SET deployment_json = @deploymentJson WHERE id = @id;",
                new { deploymentJson = rewritten, id = checkpoint.Id },
                shardTx);
        }

        shardTx.Commit();
    }

    public string RewriteImportedDeploymentForTargetStack(string deploymentJson, string targetOwner, string targetProject, string targetStack)
    {
        if (JsonNode.Parse(deploymentJson) is not JsonObject root)
        {
            return deploymentJson;
        }

        RewriteImportedSecretsProviderState(root, targetOwner, targetProject, targetStack);
        return root.ToJsonString();
    }

    public void DeleteStack(string org, string project, string stack)
    {
        using var connection = database.OpenConnection();
        using var tx = connection.BeginImmediateTransaction();
        var stackRow = connection.QuerySingleOrDefault<StackDeleteRow>(
            """
            SELECT
                s.id AS StackId,
                s.shard_path AS ShardPath
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users us ON us.id = s.org_id
            WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            LIMIT 1;
            """,
            new { org, project, stack },
            tx);
        if (stackRow is null)
        {
            tx.Rollback();
            return;
        }

        connection.Execute("DELETE FROM deployment_run_locations WHERE stack_id = @stackId;", new { stackId = stackRow.StackId }, tx);
        connection.Execute("DELETE FROM update_locations WHERE stack_id = @stackId;", new { stackId = stackRow.StackId }, tx);
        connection.Execute("DELETE FROM stacks WHERE id = @stackId;", new { stackId = stackRow.StackId }, tx);
        tx.Commit();

        if (!string.IsNullOrWhiteSpace(stackRow.ShardPath))
        {
            stackShardDatabase.DeleteShard(stackRow.ShardPath);
        }
    }

    private string EnsureProjectByNamespaceId(string namespaceId, string project)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var existing = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM projects WHERE org_id = @namespaceId AND name = @project LIMIT 1;",
            new { namespaceId, project },
            transaction);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var projectId = Guid.NewGuid().ToString("N");
        connection.Execute(
            "INSERT INTO projects (id, org_id, name) VALUES (@id, @namespaceId, @name);",
            new { id = projectId, namespaceId, name = project },
            transaction);
        transaction.Commit();
        return projectId;
    }

    private string EnsureProject(string org, string project)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var existing = connection.QuerySingleOrDefault<string>(
            """
            SELECT p.id
            FROM projects p
            LEFT JOIN orgs o ON o.id = p.org_id
            LEFT JOIN users u ON u.id = p.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project
            LIMIT 1;
            """,
            new { org, project },
            transaction);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var namespaceId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @org LIMIT 1;",
            new { org },
            transaction)
            ?? connection.QuerySingleOrDefault<string>(
                "SELECT id FROM users WHERE github_login = @org LIMIT 1;",
                new { org },
                transaction);
        if (string.IsNullOrWhiteSpace(namespaceId))
        {
            throw new InvalidOperationException("Organization not found");
        }

        var projectId = Guid.NewGuid().ToString("N");
        connection.Execute(
            "INSERT INTO projects (id, org_id, name) VALUES (@id, @orgId, @name);",
            new { id = projectId, orgId = namespaceId, name = project },
            transaction);
        transaction.Commit();
        return projectId;
    }

    private static string RewriteDeploymentForRenamedStack(
        string deploymentJson,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        if (JsonNode.Parse(deploymentJson) is not JsonObject root)
        {
            return deploymentJson;
        }

        RewriteResourceArray(root["resources"] as JsonArray, oldProject, oldStack, newProject, newStack);

        if (root["pendingOperations"] is JsonArray pendingOperations)
        {
            RewritePendingOperations(pendingOperations, oldProject, oldStack, newProject, newStack);
        }

        if (root["pending_operations"] is JsonArray legacyPendingOperations)
        {
            RewritePendingOperations(legacyPendingOperations, oldProject, oldStack, newProject, newStack);
        }

        RewriteSecretsProviderState(root, oldProject, oldStack, newProject, newStack);
        return root.ToJsonString();
    }

    private static void RewriteResourceArray(JsonArray? resources, string oldProject, string oldStack, string newProject, string newStack)
    {
        if (resources is null)
        {
            return;
        }

        foreach (var node in resources)
        {
            if (node is JsonObject resource)
            {
                RewriteResourceObject(resource, oldProject, oldStack, newProject, newStack);
            }
        }
    }

    private static void RewritePendingOperations(JsonArray operations, string oldProject, string oldStack, string newProject, string newStack)
    {
        foreach (var node in operations)
        {
            if (node is not JsonObject operation || operation["resource"] is not JsonObject resource)
            {
                continue;
            }

            RewriteResourceObject(resource, oldProject, oldStack, newProject, newStack);
        }
    }

    private static void RewriteResourceObject(JsonObject resource, string oldProject, string oldStack, string newProject, string newStack)
    {
        RewriteStringProperty(resource, "urn", value => RewriteUrn(value, oldProject, oldStack, newProject, newStack));
        RewriteStringProperty(resource, "parent", value => RewriteUrn(value, oldProject, oldStack, newProject, newStack));
        RewriteStringProperty(resource, "deletedWith", value => RewriteUrn(value, oldProject, oldStack, newProject, newStack));
        RewriteStringProperty(resource, "provider", value => RewriteProviderReference(value, oldProject, oldStack, newProject, newStack));
        RewriteUrnArray(resource["dependencies"] as JsonArray, oldProject, oldStack, newProject, newStack);
        RewriteUrnArray(resource["replaceWith"] as JsonArray, oldProject, oldStack, newProject, newStack);

        if (resource["propertyDependencies"] is JsonObject propertyDependencies)
        {
            foreach (var dependencyList in propertyDependencies)
            {
                RewriteUrnArray(dependencyList.Value as JsonArray, oldProject, oldStack, newProject, newStack);
            }
        }
    }

    private static void RewriteUrnArray(JsonArray? values, string oldProject, string oldStack, string newProject, string newStack)
    {
        if (values is null)
        {
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is JsonValue value && value.TryGetValue<string>(out var urn))
            {
                values[i] = RewriteUrn(urn, oldProject, oldStack, newProject, newStack);
            }
        }
    }

    private static void RewriteStringProperty(JsonObject node, string propertyName, Func<string, string> rewrite)
    {
        if (node[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            node[propertyName] = rewrite(text);
        }
    }

    private static string RewriteProviderReference(string providerReference, string oldProject, string oldStack, string newProject, string newStack)
    {
        var separator = providerReference.LastIndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return RewriteUrn(providerReference, oldProject, oldStack, newProject, newStack);
        }

        var urn = providerReference[..separator];
        var id = providerReference[(separator + 2)..];
        var rewrittenUrn = RewriteUrn(urn, oldProject, oldStack, newProject, newStack);
        return rewrittenUrn == urn ? providerReference : $"{rewrittenUrn}::{id}";
    }

    private static string RewriteUrn(string urn, string oldProject, string oldStack, string newProject, string newStack)
    {
        const string prefix = "urn:pulumi:";
        if (!urn.StartsWith(prefix, StringComparison.Ordinal))
        {
            return urn;
        }

        var stackSeparator = urn.IndexOf("::", prefix.Length, StringComparison.Ordinal);
        if (stackSeparator < 0)
        {
            return urn;
        }

        var projectSeparator = urn.IndexOf("::", stackSeparator + 2, StringComparison.Ordinal);
        if (projectSeparator < 0)
        {
            return urn;
        }

        var typeSeparator = urn.IndexOf("::", projectSeparator + 2, StringComparison.Ordinal);
        if (typeSeparator < 0)
        {
            return urn;
        }

        var stackName = urn[prefix.Length..stackSeparator];
        var projectName = urn[(stackSeparator + 2)..projectSeparator];
        if (!string.Equals(stackName, oldStack, StringComparison.Ordinal) ||
            !string.Equals(projectName, oldProject, StringComparison.Ordinal))
        {
            return urn;
        }

        var resourceType = urn[(projectSeparator + 2)..typeSeparator];
        var resourceName = urn[(typeSeparator + 2)..];
        if (string.Equals(resourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal))
        {
            resourceName = $"{newProject}-{newStack}";
        }

        return $"{prefix}{newStack}::{newProject}::{resourceType}::{resourceName}";
    }

    private static void RewriteSecretsProviderState(JsonObject root, string oldProject, string oldStack, string newProject, string newStack)
    {
        var state = GetSecretsProviderState(root);
        if (state?["url"] is not JsonValue value || !value.TryGetValue<string>(out var url))
        {
            return;
        }

        var oldSegment = $"/project/{oldProject}/stack/{oldStack}";
        var newSegment = $"/project/{newProject}/stack/{newStack}";
        if (url.Contains(oldSegment, StringComparison.Ordinal))
        {
            state["url"] = url.Replace(oldSegment, newSegment, StringComparison.Ordinal);
        }
    }

    private static void RewriteImportedSecretsProviderState(JsonObject root, string targetOwner, string targetProject, string targetStack)
    {
        var secretsProviders = root["secrets_providers"] as JsonObject ?? root["secretsProviders"] as JsonObject;
        if (secretsProviders is null)
        {
            return;
        }

        var type = secretsProviders["type"]?.GetValue<string>();
        if (!string.Equals(type, "service", StringComparison.Ordinal))
        {
            return;
        }

        var state = GetSecretsProviderState(root);
        if (state is null)
        {
            return;
        }

        state["owner"] = targetOwner;
        state["project"] = targetProject;
        state["stack"] = targetStack;
    }

    private static JsonObject? GetSecretsProviderState(JsonObject root)
    {
        var secretsProviders = root["secrets_providers"] as JsonObject ?? root["secretsProviders"] as JsonObject;
        return secretsProviders?["state"] as JsonObject;
    }
}
