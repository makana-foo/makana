using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makana.Api.Updates;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(string))]
internal partial class UpdateViewMaterializerJsonContext : JsonSerializerContext;
