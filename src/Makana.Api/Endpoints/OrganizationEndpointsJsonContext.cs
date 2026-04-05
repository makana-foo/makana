using System.Text.Json.Serialization;
using Makana.Api.Contracts;

namespace Makana.Api.Endpoints;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CreateOrganizationRequest))]
[JsonSerializable(typeof(CreateOrganizationResponse))]
[JsonSerializable(typeof(OrganizationMembersResponse))]
[JsonSerializable(typeof(OrganizationMemberResponse[]))]
[JsonSerializable(typeof(OrganizationInviteResponse))]
[JsonSerializable(typeof(OrganizationInvitesResponse))]
[JsonSerializable(typeof(CreateOrganizationInviteRequest))]
[JsonSerializable(typeof(ClaimOrganizationMembershipResponse))]
[JsonSerializable(typeof(UpdateOrganizationMemberRequest))]
[JsonSerializable(typeof(CreateOrganizationTokenRequest))]
[JsonSerializable(typeof(CreateOrganizationTokenResponse))]
[JsonSerializable(typeof(OrganizationTokensResponse))]
[JsonSerializable(typeof(OrganizationTokenSummaryResponse[]))]
[JsonSerializable(typeof(OrganizationOidcConfigurationRequest))]
[JsonSerializable(typeof(OrganizationSamlConfigurationRequest))]
[JsonSerializable(typeof(UpdateOrganizationSsoRequest))]
[JsonSerializable(typeof(OrganizationSsoResponse))]
[JsonSerializable(typeof(OrganizationLoginDiscoveryResponse))]
[JsonSerializable(typeof(ScimUserRequest))]
[JsonSerializable(typeof(ScimUserResponse))]
[JsonSerializable(typeof(ScimServiceProviderConfigResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class OrganizationEndpointsJsonContext : JsonSerializerContext
{
}
