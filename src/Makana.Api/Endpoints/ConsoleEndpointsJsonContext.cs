using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GitHubAppIntegration))]
internal partial class ConsoleEndpointsJsonContext : JsonSerializerContext
{
}
