using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CreateDeploymentRequest))]
[JsonSerializable(typeof(DeploymentSettingsRequest))]
[JsonSerializable(typeof(CreateDeploymentResponse))]
[JsonSerializable(typeof(DeploymentLogs))]
[JsonSerializable(typeof(SecretValueResponse))]
internal partial class DeploymentEndpointsJsonContext : JsonSerializerContext
{
}
