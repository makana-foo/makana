using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Makana.Api.Security;
using Makana.Api.Contracts;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class EndpointHelpers
{
    public static async Task<IResult> CreateUpdateResponseAsync(
        BackendStore store,
        string org,
        string project,
        string stack,
        string kind,
        UpdateProgramRequest request)
    {
        var ids = await store.GetStackIdsAsync(org, project, stack);
        if (ids is null)
        {
            return Results.NotFound();
        }

        var updateId = await store.CreateUpdateAsync(
            ids.Value.StackId,
            kind,
            JsonSerializer.Serialize(request, EndpointHelpersJsonContext.Default.UpdateProgramRequest));
        return Results.Ok(new UpdateProgramResponse(updateId, null, null, null));
    }

    public static async Task<IResult> CreateUpdateResponseByNamespaceIdAsync(
        BackendStore store,
        string namespaceId,
        string project,
        string stack,
        string kind,
        UpdateProgramRequest request)
    {
        var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceId, project, stack);
        if (ids is null)
        {
            return Results.NotFound();
        }

        var updateId = await store.CreateUpdateAsync(
            ids.Value.StackId,
            kind,
            JsonSerializer.Serialize(request, EndpointHelpersJsonContext.Default.UpdateProgramRequest));
        return Results.Ok(new UpdateProgramResponse(updateId, null, null, null));
    }

    public static string GetBaseUrl(HttpRequest request)
    {
        return $"{request.Scheme}://{request.Host}";
    }

    public static async Task<StackSummary[]> BuildStackSummariesAsync(
        BackendStore store,
        HttpRequest request,
        IEnumerable<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)> rows)
    {
        var list = new List<StackSummary>();
        foreach (var row in rows)
        {
            var tags = row.TagsJson is null
                ? null
                : JsonSerializer.Deserialize(row.TagsJson, EndpointHelpersJsonContext.Default.DictionaryStringString);
            var lastUpdate = await store.GetLatestUpdateTimeAsync(row.StackId);
            var resourceCount = await GetResourceCountAsync(store, row.StackId);
            var link = new StackLinks($"{GetBaseUrl(request)}/stacks/{row.OrgName}/{row.ProjectName}/{row.Name}");
            list.Add(new StackSummary(
                $"{row.OrgName}/{row.ProjectName}/{row.Name}",
                row.Name,
                row.ProjectName,
                row.OrgName,
                ToUnixTimeSeconds(lastUpdate),
                resourceCount,
                link,
                tags));
        }

        return list.ToArray();
    }

    public static async Task<int?> GetResourceCountAsync(BackendStore store, string stackId)
    {
        var deployment = await store.GetLatestDeploymentAsync(stackId);
        if (deployment is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(deployment.Value.DeploymentJson);
            if (doc.RootElement.TryGetProperty("resources", out var resources) &&
                resources.ValueKind == JsonValueKind.Array)
            {
                return resources.GetArrayLength();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public static long? ToUnixTimeSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed.ToUnixTimeSeconds();
        }

        return null;
    }

    public static string NormalizeUpdateStatus(string status)
    {
        return status switch
        {
            "not-started" => "not started",
            "in-progress" => "running",
            _ => status
        };
    }

    public static Dictionary<string, StackConfigValue>? ExtractConfig(string? programRequestJson)
    {
        if (string.IsNullOrWhiteSpace(programRequestJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(programRequestJson);
            if (!doc.RootElement.TryGetProperty("config", out var configElement))
            {
                return null;
            }

            return JsonSerializer.Deserialize(
                configElement.GetRawText(),
                EndpointHelpersJsonContext.Default.DictionaryStringStackConfigValue);
        }
        catch
        {
            return null;
        }
    }

    public static (string? Message, Dictionary<string, string>? Environment) ExtractMetadata(string? programRequestJson)
    {
        if (string.IsNullOrWhiteSpace(programRequestJson))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(programRequestJson);
            if (!doc.RootElement.TryGetProperty("metadata", out var metadataElement))
            {
                return (null, null);
            }

            string? message = null;
            if (metadataElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            Dictionary<string, string>? environment = null;
            if (metadataElement.TryGetProperty("environment", out var envElement))
            {
                environment = JsonSerializer.Deserialize(
                    envElement.GetRawText(),
                    EndpointHelpersJsonContext.Default.DictionaryStringString);
            }

            return (message, environment);
        }
        catch
        {
            return (null, null);
        }
    }

    public static string ExtractSecretPlaintext(JsonElement request)
    {
        if (request.ValueKind == JsonValueKind.String)
        {
            return request.GetString() ?? string.Empty;
        }

        if (request.ValueKind == JsonValueKind.Object)
        {
            if (request.TryGetProperty("secret", out var secretElement) && secretElement.ValueKind == JsonValueKind.String)
            {
                return secretElement.GetString() ?? string.Empty;
            }

            if (request.TryGetProperty("ciphertext", out var cipherElement) && cipherElement.ValueKind == JsonValueKind.String)
            {
                return cipherElement.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public static async Task SaveUploadAsync(BackendStore store, HttpRequest request, string path)
    {
        await using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream);
        await store.SaveUploadAsync(path, request.ContentType, stream.ToArray());
    }

    public static string? GetUpdateToken(HttpContext context)
    {
        var kind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value
            ?? context.Items["auth.kind"] as string;
        if (string.Equals(kind, "update-token", StringComparison.OrdinalIgnoreCase))
        {
            return context.User.FindFirst(PulumiAuthenticationDefaults.AuthTokenClaim)?.Value
                ?? context.Items["auth.token"] as string;
        }
        return null;
    }

    public static byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        var output = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, output, nonce.Length + tag.Length, cipher.Length);
        return output;
    }

    public static byte[] Decrypt(byte[] key, byte[] ciphertext)
    {
        if (ciphertext.Length < 28)
        {
            return Array.Empty<byte>();
        }

        var nonce = new byte[12];
        var tag = new byte[16];
        var cipher = new byte[ciphertext.Length - 28];

        Buffer.BlockCopy(ciphertext, 0, nonce, 0, nonce.Length);
        Buffer.BlockCopy(ciphertext, nonce.Length, tag, 0, tag.Length);
        Buffer.BlockCopy(ciphertext, nonce.Length + tag.Length, cipher, 0, cipher.Length);

        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    public static string CreateEmptyDeploymentJson()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        const string version = "0.0.0";
        var magic = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(version))).ToLowerInvariant();
        return $"{{\"manifest\":{{\"time\":\"{timestamp}\",\"magic\":\"{magic}\",\"version\":\"{version}\"}},\"resources\":[],\"pending_operations\":[]}}";
    }

    public static string CreateEmptyDeploymentJson(string? baseDeploymentJson)
    {
        if (string.IsNullOrWhiteSpace(baseDeploymentJson))
        {
            return CreateEmptyDeploymentJson();
        }

        try
        {
            var node = JsonNode.Parse(baseDeploymentJson) as JsonObject;
            if (node is null)
            {
                return CreateEmptyDeploymentJson();
            }

            node["resources"] = new JsonArray();
            node["pending_operations"] = new JsonArray();
            return node.ToJsonString();
        }
        catch
        {
            return CreateEmptyDeploymentJson();
        }
    }

    public static CapabilitiesResponse GetCapabilitiesResponse()
    {
        var deploymentConfig = JsonDocument.Parse("{\"version\":4}").RootElement.Clone();
        return new CapabilitiesResponse(new[]
        {
            new ApiCapabilityConfig("batch-encrypt", null, null),
            new ApiCapabilityConfig("deployment-schema-version", 1, deploymentConfig),
        });
    }

    public static bool TryApplyTextEdits(string source, JsonElement editsElement, out string result)
    {
        result = source;
        if (editsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var edits = new List<(int Start, int End, string NewText)>();
        foreach (var editElement in editsElement.EnumerateArray())
        {
            if (editElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetEditSpan(editElement, source, out var start, out var end))
            {
                return false;
            }

            if (!TryGetEditText(editElement, out var newText))
            {
                return false;
            }

            edits.Add((start, end, newText));
        }

        edits.Sort((left, right) => right.Start.CompareTo(left.Start));
        var current = source;
        foreach (var edit in edits)
        {
            if (edit.Start < 0 || edit.End < edit.Start || edit.End > current.Length)
            {
                return false;
            }

            current = string.Concat(
                current.AsSpan(0, edit.Start),
                edit.NewText,
                current.AsSpan(edit.End));
        }

        result = current;
        return true;
    }

    private static bool TryGetEditText(JsonElement editElement, out string newText)
    {
        if (TryGetProperty(editElement, "newText", out var newTextElement) ||
            TryGetProperty(editElement, "NewText", out newTextElement))
        {
            if (newTextElement.ValueKind == JsonValueKind.String)
            {
                newText = newTextElement.GetString() ?? string.Empty;
                return true;
            }
        }

        newText = string.Empty;
        return false;
    }

    private static bool TryGetEditSpan(JsonElement editElement, string source, out int start, out int end)
    {
        start = 0;
        end = 0;

        if (!TryGetProperty(editElement, "span", out var spanElement) &&
            !TryGetProperty(editElement, "Span", out spanElement))
        {
            return false;
        }

        if (!TryGetSpanOffset(spanElement, "start", source, out start) &&
            !TryGetSpanOffset(spanElement, "Start", source, out start))
        {
            return false;
        }

        if (!TryGetSpanOffset(spanElement, "end", source, out end) &&
            !TryGetSpanOffset(spanElement, "End", source, out end))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetSpanOffset(JsonElement spanElement, string name, string source, out int offset)
    {
        offset = 0;
        if (!TryGetProperty(spanElement, name, out var point))
        {
            return false;
        }

        if (point.ValueKind == JsonValueKind.Number)
        {
            offset = point.GetInt32();
            return true;
        }

        if (point.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetProperty(point, "offset", out var offsetElement) ||
            TryGetProperty(point, "Offset", out offsetElement) ||
            TryGetProperty(point, "index", out offsetElement) ||
            TryGetProperty(point, "Index", out offsetElement))
        {
            if (offsetElement.ValueKind == JsonValueKind.Number)
            {
                offset = offsetElement.GetInt32();
                return true;
            }
        }

        if (!TryGetLineColumn(point, out var line, out var column))
        {
            return false;
        }

        offset = LineColumnToOffset(source, line, column);
        return offset >= 0;
    }

    private static bool TryGetLineColumn(JsonElement point, out int line, out int column)
    {
        line = 0;
        column = 0;

        if (!(TryGetProperty(point, "line", out var lineElement) ||
              TryGetProperty(point, "Line", out lineElement)))
        {
            return false;
        }

        if (lineElement.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        line = lineElement.GetInt32();

        if (TryGetProperty(point, "column", out var columnElement) ||
            TryGetProperty(point, "Column", out columnElement) ||
            TryGetProperty(point, "character", out columnElement) ||
            TryGetProperty(point, "Character", out columnElement))
        {
            if (columnElement.ValueKind == JsonValueKind.Number)
            {
                column = columnElement.GetInt32();
                return true;
            }
        }

        return false;
    }

    private static int LineColumnToOffset(string source, int line, int column)
    {
        if (line < 0 || column < 0)
        {
            return -1;
        }

        var currentLine = 0;
        var index = 0;
        while (index < source.Length && currentLine < line)
        {
            if (source[index] == '\n')
            {
                currentLine++;
            }
            index++;
        }

        if (currentLine != line)
        {
            return -1;
        }

        if (index + column > source.Length)
        {
            return -1;
        }

        return index + column;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
