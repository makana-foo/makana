using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CreateStackRequest))]
[JsonSerializable(typeof(StackRenameRequest))]
[JsonSerializable(typeof(StackConfig))]
[JsonSerializable(typeof(UntypedDeployment))]
[JsonSerializable(typeof(EncryptValueRequest))]
[JsonSerializable(typeof(EncryptValueResponse))]
[JsonSerializable(typeof(DecryptValueRequest))]
[JsonSerializable(typeof(DecryptValueResponse))]
[JsonSerializable(typeof(BatchEncryptRequest))]
[JsonSerializable(typeof(BatchEncryptResponse))]
[JsonSerializable(typeof(BatchDecryptResponse))]
[JsonSerializable(typeof(LogDecryptionEventRequest))]
[JsonSerializable(typeof(Stack))]
[JsonSerializable(typeof(ListStacksResponse))]
[JsonSerializable(typeof(LogsResult))]
[JsonSerializable(typeof(ExportStackResponse))]
[JsonSerializable(typeof(ImportStackResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, StackConfigValue>))]
[JsonSerializable(typeof(Dictionary<string, byte[]>))]
internal partial class StackEndpointsJsonContext : JsonSerializerContext
{
}
