using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

internal static class UpdateRepositoryCommon
{
    public static bool IsPreviewKind(string kind) =>
        string.Equals(kind, "preview", StringComparison.OrdinalIgnoreCase);

    public static bool? TryGetPreviewProgramRequest(string? programRequestJson)
    {
        if (string.IsNullOrWhiteSpace(programRequestJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(programRequestJson);
            if (!doc.RootElement.TryGetProperty("options", out var optionsElement) ||
                optionsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (optionsElement.TryGetProperty("dryRun", out var dryRunElement) &&
                dryRunElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return dryRunElement.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static bool IsPreviewOperation(string kind, string? programRequestJson)
    {
        var dryRun = TryGetPreviewProgramRequest(programRequestJson);
        return dryRun ?? IsPreviewKind(kind);
    }

    public static int GetCurrentStackVersion(SqliteConnection connection, SqliteTransaction? transaction, string stackId)
    {
        var rows = connection.Query<UpdateVersionKindRow>(
            """
            SELECT
                version AS Version,
                kind AS Kind,
                program_request_json AS ProgramRequestJson
            FROM updates
            WHERE stack_id = @stackId
              AND version > 0
            UNION ALL
            SELECT
                version AS Version,
                'deployment' AS Kind,
                NULL AS ProgramRequestJson
            FROM deployments
            WHERE stack_id = @stackId;
            """,
            new { stackId },
            transaction);
        var currentVersion = 0;
        foreach (var row in rows)
        {
            if (string.Equals(row.Kind, "deployment", StringComparison.OrdinalIgnoreCase) ||
                !IsPreviewOperation(row.Kind, row.ProgramRequestJson))
            {
                currentVersion = Math.Max(currentVersion, checked((int)row.Version));
            }
        }

        return currentVersion;
    }
}
