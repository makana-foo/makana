using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Storage;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Dictionary<string, StackConfigValue>))]
internal partial class StorageJsonContext : JsonSerializerContext
{
}
