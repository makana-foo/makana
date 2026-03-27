using Dapper;

namespace Makana.Api.Storage;

public sealed class RegistryRepository(SqliteControlDatabase database)
{
    public void CreateRegistryOperation(string operationId, string kind, string? payloadJson)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO registry_ops (id, kind, payload_json, created_at)
            VALUES (@id, @kind, jsonb(@payload), @created);
            """,
            new
            {
                id = operationId,
                kind,
                payload = payloadJson,
                created = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public void CompleteRegistryOperation(string operationId)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            "UPDATE registry_ops SET completed_at = @completed WHERE id = @id;",
            new { id = operationId, completed = DateTimeOffset.UtcNow.ToString("O") });
    }
}
