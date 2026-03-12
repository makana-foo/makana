namespace Makana.Api.Contracts;

public sealed record StackResourceState(
    string Urn,
    string Type,
    bool Deleted,
    IReadOnlyDictionary<string, string> Outputs);

public sealed record StackResourceOutputEntry(
    string Type,
    IReadOnlyDictionary<string, string> Outputs);

public static class StackResourceOutputCollector
{
    public static IReadOnlyDictionary<string, StackResourceOutputEntry> Collect(IEnumerable<StackResourceState> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var outputs = new Dictionary<string, StackResourceOutputEntry>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            if (resource.Deleted)
            {
                continue;
            }

            outputs[resource.Urn] = new StackResourceOutputEntry(resource.Type, resource.Outputs);
        }

        return outputs;
    }
}
