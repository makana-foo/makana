using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UpdateProgramRequest))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, StackConfigValue>))]
internal partial class EndpointHelpersJsonContext : JsonSerializerContext
{
}
