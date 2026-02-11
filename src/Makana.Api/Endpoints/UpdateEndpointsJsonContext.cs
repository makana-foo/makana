using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UpdateProgramRequest))]
[JsonSerializable(typeof(UpdateProgramResponse))]
[JsonSerializable(typeof(StartUpdateRequest))]
[JsonSerializable(typeof(StartUpdateResponse))]
[JsonSerializable(typeof(RenewUpdateLeaseRequest))]
[JsonSerializable(typeof(RenewUpdateLeaseResponse))]
[JsonSerializable(typeof(PatchUpdateCheckpointRequest))]
[JsonSerializable(typeof(PatchUpdateCheckpointVerbatimRequest))]
[JsonSerializable(typeof(PatchUpdateCheckpointDeltaRequest))]
[JsonSerializable(typeof(JournalEntriesRequest))]
[JsonSerializable(typeof(EngineEventBatch))]
[JsonSerializable(typeof(CompleteUpdateRequest))]
[JsonSerializable(typeof(UpdateResults))]
[JsonSerializable(typeof(GetUpdateEventsResponse))]
[JsonSerializable(typeof(GetHistoryResponse))]
[JsonSerializable(typeof(LatestUpdateInfo))]
[JsonSerializable(typeof(UpdateInfo))]
[JsonSerializable(typeof(UpdateContentsFilesResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class UpdateEndpointsJsonContext : JsonSerializerContext
{
}
