using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Security;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class SecurityJsonContext : JsonSerializerContext
{
}
