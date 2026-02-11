using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ListPolicyGroupsResponse))]
[JsonSerializable(typeof(ListPolicyPacksResponse))]
[JsonSerializable(typeof(CreatePolicyPackResponse))]
[JsonSerializable(typeof(PolicyPackConfigSchemaResponse))]
[JsonSerializable(typeof(ResourceSearchResponse))]
[JsonSerializable(typeof(PulumiQueryResponse))]
[JsonSerializable(typeof(ListOrgTemplatesResponse))]
[JsonSerializable(typeof(ListPackagesResponse))]
[JsonSerializable(typeof(PackageMetadata))]
[JsonSerializable(typeof(StartPackagePublishRequest))]
[JsonSerializable(typeof(StartPackagePublishResponse))]
[JsonSerializable(typeof(CompletePackagePublishRequest))]
[JsonSerializable(typeof(ListTemplatesResponse))]
[JsonSerializable(typeof(TemplateMetadata))]
[JsonSerializable(typeof(StartTemplatePublishRequest))]
[JsonSerializable(typeof(StartTemplatePublishResponse))]
[JsonSerializable(typeof(PublishTemplateVersionCompleteRequest))]
internal partial class RegistryEndpointsJsonContext : JsonSerializerContext
{
}
