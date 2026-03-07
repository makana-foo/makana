using System.Text.Json.Serialization;

namespace Makana.StackUpdates.Pulumi;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(EngineEvent))]
[JsonSerializable(typeof(EngineEvent[]))]
[JsonSerializable(typeof(EngineEventBatch))]
[JsonSerializable(typeof(GetUpdateEventsResponse))]
public partial class PulumiEngineEventJsonContext : JsonSerializerContext
{
}
