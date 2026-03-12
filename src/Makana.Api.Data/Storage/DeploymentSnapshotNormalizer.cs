using System.Text.Json.Nodes;

namespace Makana.Api.Storage;

internal static class DeploymentSnapshotNormalizer
{
    public static string NormalizeForPersistence(string deploymentJson)
    {
        if (string.IsNullOrWhiteSpace(deploymentJson))
        {
            return deploymentJson;
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(deploymentJson);
        }
        catch
        {
            return deploymentJson;
        }

        if (rootNode is not JsonObject rootObject)
        {
            return deploymentJson;
        }

        if (rootObject["resources"] is not JsonArray resourcesArray)
        {
            return deploymentJson;
        }

        var resourceObjects = resourcesArray
            .OfType<JsonObject>()
            .ToArray();
        var knownUrns = resourceObjects
            .Select(static resource => resource["urn"]?.GetValue<string>())
            .Where(static urn => !string.IsNullOrWhiteSpace(urn))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        var changed = false;
        foreach (var resource in resourceObjects)
        {
            var parentUrn = resource["parent"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(parentUrn) || knownUrns.Contains(parentUrn))
            {
                continue;
            }

            resource.Remove("parent");
            changed = true;

            var urn = resource["urn"]?.GetValue<string>();
            var normalizedUrn = NormalizeDanglingParentUrn(urn);
            if (!string.Equals(urn, normalizedUrn, StringComparison.Ordinal))
            {
                resource["urn"] = normalizedUrn;
            }
        }

        return changed ? rootObject.ToJsonString() : deploymentJson;
    }

    private static string? NormalizeDanglingParentUrn(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return urn;
        }

        const string prefix = "urn:pulumi:";
        if (!urn.StartsWith(prefix, StringComparison.Ordinal))
        {
            return urn;
        }

        var remainder = urn[prefix.Length..];
        var parts = remainder.Split(["::"], StringSplitOptions.None);
        if (parts.Length < 4)
        {
            return urn;
        }

        var qualifiedType = parts[2];
        var separator = qualifiedType.LastIndexOf('$');
        if (separator < 0)
        {
            return urn;
        }

        parts[2] = qualifiedType[(separator + 1)..];
        return prefix + string.Join("::", parts);
    }
}
