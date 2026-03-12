using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ServiceUser))]
[JsonSerializable(typeof(UpdateUserThemeRequest))]
[JsonSerializable(typeof(ListStacksResponse))]
[JsonSerializable(typeof(GetDefaultOrganizationResponse))]
[JsonSerializable(typeof(CliVersionResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(TokenExchangeGrantResponse))]
[JsonSerializable(typeof(CapabilitiesResponse))]
internal partial class CoreEndpointsJsonContext : JsonSerializerContext
{
}
