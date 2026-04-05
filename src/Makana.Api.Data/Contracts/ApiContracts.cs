using System.Text.Json;
using System.Text.Json.Serialization;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Contracts;

public record ErrorResponse(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("requestId")] string? RequestId
);

public record ServiceUserInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl,
    [property: JsonPropertyName("email")] string? Email
);

public record ServiceThemeOption(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("stylesheetPath")] string StylesheetPath
);

public record UpdateUserThemeRequest(
    [property: JsonPropertyName("theme")] string Theme
);

public record ServiceTokenInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("organization")] string? Organization,
    [property: JsonPropertyName("team")] string? Team
);

public record CreateOrganizationRequest(
    [property: JsonPropertyName("name")] string Name
);

public record CreateOrganizationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("redirectPath")] string RedirectPath
);

public record OrganizationMemberResponse(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("membershipKind")] string MembershipKind
);

public record OrganizationMembersResponse(
    [property: JsonPropertyName("members")] OrganizationMemberResponse[] Members
);

public record CreateOrganizationInviteRequest(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role
);

public record OrganizationInviteResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("createdAt")] string CreatedAt,
    [property: JsonPropertyName("claimed")] bool Claimed,
    [property: JsonPropertyName("revoked")] bool Revoked
);

public record OrganizationInvitesResponse(
    [property: JsonPropertyName("invites")] OrganizationInviteResponse[] Invites
);

public record ClaimOrganizationMembershipResponse(
    [property: JsonPropertyName("claimedMemberships")] int ClaimedMemberships
);

public record UpdateOrganizationMemberRequest(
    [property: JsonPropertyName("role")] string Role
);

public record CreateOrganizationTokenRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("expiresInDays")] int? ExpiresInDays
);

public record OrganizationTokenSummaryResponse(
    [property: JsonPropertyName("tokenId")] string TokenId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("createdByUserId")] string? CreatedByUserId,
    [property: JsonPropertyName("issuedAt")] string IssuedAt,
    [property: JsonPropertyName("expiresAt")] string? ExpiresAt,
    [property: JsonPropertyName("revoked")] bool Revoked
);

public record OrganizationTokensResponse(
    [property: JsonPropertyName("tokens")] OrganizationTokenSummaryResponse[] Tokens
);

public record CreateOrganizationTokenResponse(
    [property: JsonPropertyName("tokenId")] string TokenId,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("expiresAt")] string? ExpiresAt
);

public record OrganizationOidcConfigurationRequest(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("clientSecret")] string ClientSecret
);

public record OrganizationSamlConfigurationRequest(
    [property: JsonPropertyName("ssoUrl")] string SsoUrl,
    [property: JsonPropertyName("caCertificatePem")] string CaCertificatePem,
    [property: JsonPropertyName("usernameAttribute")] string UsernameAttribute,
    [property: JsonPropertyName("emailAttribute")] string EmailAttribute,
    [property: JsonPropertyName("groupsAttribute")] string? GroupsAttribute,
    [property: JsonPropertyName("entityIssuer")] string? EntityIssuer,
    [property: JsonPropertyName("ssoIssuer")] string? SsoIssuer,
    [property: JsonPropertyName("groupsDelimiter")] string? GroupsDelimiter,
    [property: JsonPropertyName("nameIdPolicyFormat")] string? NameIdPolicyFormat
);

public record UpdateOrganizationSsoRequest(
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("domains")] string[]? Domains,
    [property: JsonPropertyName("scimBearerToken")] string? ScimBearerToken,
    [property: JsonPropertyName("oidc")] OrganizationOidcConfigurationRequest? Oidc,
    [property: JsonPropertyName("saml")] OrganizationSamlConfigurationRequest? Saml
);

public record OrganizationSsoResponse(
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("connectionId")] string? ConnectionId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("domains")] string[] Domains,
    [property: JsonPropertyName("hasScimBearerToken")] bool HasScimBearerToken,
    [property: JsonPropertyName("scimBearerToken")] string? ScimBearerToken,
    [property: JsonPropertyName("oidc")] OrganizationOidcConfigurationRequest? Oidc,
    [property: JsonPropertyName("saml")] OrganizationSamlConfigurationRequest? Saml
);

public record OrganizationLoginDiscoveryResponse(
    [property: JsonPropertyName("organization")] string Organization,
    [property: JsonPropertyName("protocol")] string Protocol,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("connectionId")] string? ConnectionId,
    [property: JsonPropertyName("displayName")] string? DisplayName
);

public record ScimUserEmail(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("primary")] bool? Primary
);

public record ScimUserRequest(
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("active")] bool? Active,
    [property: JsonPropertyName("emails")] ScimUserEmail[]? Emails
);

public record ScimUserResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("externalId")] string ExternalId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("active")] bool Active
);

public record ScimServiceProviderConfigResponse(
    [property: JsonPropertyName("schemas")] string[] Schemas,
    [property: JsonPropertyName("patch")] object Patch,
    [property: JsonPropertyName("filter")] object Filter
);

public record ServiceUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl,
    [property: JsonPropertyName("organizations")] ServiceUserInfo[] Organizations,
    [property: JsonPropertyName("theme")] string? Theme,
    [property: JsonPropertyName("availableThemes")] ServiceThemeOption[] AvailableThemes,
    [property: JsonPropertyName("identities")] string[] Identities,
    [property: JsonPropertyName("siteAdmin")] bool? SiteAdmin,
    [property: JsonPropertyName("tokenInfo")] ServiceTokenInfo? TokenInfo
);

public record StackSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("stackName")] string StackName,
    [property: JsonPropertyName("projectName")] string ProjectName,
    [property: JsonPropertyName("orgName")] string OrgName,
    [property: JsonPropertyName("lastUpdate")] long? LastUpdate,
    [property: JsonPropertyName("resourceCount")] int? ResourceCount = null,
    [property: JsonPropertyName("links")] StackLinks? Links = null,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags = null
);

public record StackLinks(
    [property: JsonPropertyName("self")] string Self
);

public record OperationStatus(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("started")] long Started
);

public record CloudStackConfig(
    [property: JsonPropertyName("environment")] string Environment,
    [property: JsonPropertyName("secretsProvider")] string? SecretsProvider,
    [property: JsonPropertyName("encryptedKey")] string? EncryptedKey,
    [property: JsonPropertyName("encryptionSalt")] string? EncryptionSalt
);

public record Stack(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orgName")] string OrgName,
    [property: JsonPropertyName("projectName")] string ProjectName,
    [property: JsonPropertyName("stackName")] string StackName,
    [property: JsonPropertyName("currentOperation")] OperationStatus? CurrentOperation,
    [property: JsonPropertyName("activeUpdate")] string ActiveUpdate,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags,
    [property: JsonPropertyName("config")] CloudStackConfig? Config,
    [property: JsonPropertyName("version")] int Version
);

public record ListStacksResponse(
    [property: JsonPropertyName("stacks")] StackSummary[] Stacks,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);

public record StackConfigValue(
    [property: JsonPropertyName("stringValue")] string String,
    [property: JsonPropertyName("secret")] bool Secret,
    [property: JsonPropertyName("objectValue")] bool Object
);

public record StackConfig(
    [property: JsonPropertyName("config")] Dictionary<string, StackConfigValue> Config
);

public record CreateStackRequest(
    [property: JsonPropertyName("stackName")] string StackName,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags,
    [property: JsonPropertyName("teams")] string[]? Teams,
    [property: JsonPropertyName("state")] JsonElement? State,
    [property: JsonPropertyName("config")] StackConfig? Config
);

public record UpdateProgramRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("runtime")] string Runtime,
    [property: JsonPropertyName("main")] string? Main,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("config")] Dictionary<string, StackConfigValue> Config,
    [property: JsonPropertyName("options")] JsonElement Options,
    [property: JsonPropertyName("metadata")] JsonElement Metadata
);

public record UpdateProgramResponse(
    [property: JsonPropertyName("updateID")] string UpdateId,
    [property: JsonPropertyName("messages")] JsonElement? Messages,
    [property: JsonPropertyName("requiredPolicies")] JsonElement? RequiredPolicies,
    [property: JsonPropertyName("aiSettings")] JsonElement? AISettings
);

public record StartUpdateRequest(
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags,
    [property: JsonPropertyName("journalVersion")] int? JournalVersion
);

public record StartUpdateResponse(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("journalVersion")] int? JournalVersion
);

public record RenewUpdateLeaseRequest(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("duration")] int Duration
);

public record RenewUpdateLeaseResponse(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("tokenExpiration")] long TokenExpiration
);

public record PatchUpdateCheckpointRequest(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("features")] JsonElement? Features,
    [property: JsonPropertyName("deployment")] JsonElement Deployment
);

public record PatchUpdateCheckpointDeltaRequest(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("checkpointHash")] string CheckpointHash,
    [property: JsonPropertyName("sequenceNumber")] int SequenceNumber,
    [property: JsonPropertyName("deploymentDelta")] JsonElement DeploymentDelta
);

public record PatchUpdateCheckpointVerbatimRequest(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("sequenceNumber")] int SequenceNumber,
    [property: JsonPropertyName("untypedDeployment")] JsonElement UntypedDeployment
);

public record JournalEntriesRequest(
    [property: JsonPropertyName("entries")] JsonElement Entries
);

public record CompleteUpdateRequest(
    [property: JsonPropertyName("status")] string Status
);

public record EngineEventBatch(
    [property: JsonPropertyName("events")] EngineEvent[] Events
);

public record UpdateInfo(
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("isPreview")] bool? IsPreview = null,
    [property: JsonPropertyName("startTime")] long? StartTime = null,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("environment")] Dictionary<string, string>? Environment = null,
    [property: JsonPropertyName("config")] Dictionary<string, StackConfigValue>? Config = null,
    [property: JsonPropertyName("result")] string? Result = null,
    [property: JsonPropertyName("endTime")] long? EndTime = null,
    [property: JsonPropertyName("version")] int? Version = null,
    [property: JsonPropertyName("deployment")] JsonElement? Deployment = null,
    [property: JsonPropertyName("resources")] JsonElement[]? Resources = null,
    [property: JsonPropertyName("resourceChanges")] Dictionary<string, int>? ResourceChanges = null,
    [property: JsonPropertyName("resourceCount")] int? ResourceCount = null,
    [property: JsonPropertyName("updateID")] string? UpdateId = null
);

public record GetHistoryResponse(
    [property: JsonPropertyName("updates")] UpdateInfo[] Updates
);

public record LatestUpdateInfo(
    [property: JsonPropertyName("info")] UpdateInfo Info
);

public record UpdateResults(
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("version")] int? Version = null,
    [property: JsonPropertyName("events")] JsonElement[]? Events = null,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken = null
);

public record SelectedUpdateReferenceResponse(
    [property: JsonPropertyName("updateID")] string UpdateId,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("routeRunType")] string RouteRunType,
    [property: JsonPropertyName("routeReference")] string RouteReference
);

public record UpdateServiceGitActor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl
);

public record UpdateServiceGitHubCommitInfo(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("author")] UpdateServiceGitActor? Author
);

public record UpdateServiceEnvelope(
    [property: JsonPropertyName("info")] UpdateInfo Info,
    [property: JsonPropertyName("updateID")] string UpdateId,
    [property: JsonPropertyName("githubCommitInfo")] UpdateServiceGitHubCommitInfo? GitHubCommitInfo,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("latestVersion")] int LatestVersion,
    [property: JsonPropertyName("requestedBy")] ServiceUserInfo? RequestedBy,
    [property: JsonPropertyName("requestedByToken")] string RequestedByToken
);

public record UpdateVersionSummary(
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("startTime")] long StartTime,
    [property: JsonPropertyName("endTime")] long EndTime,
    [property: JsonPropertyName("resourceCount")] int ResourceCount
);

public record StackResourceCountResponse(
    [property: JsonPropertyName("resourceCount")] int ResourceCount,
    [property: JsonPropertyName("version")] int Version
);

public record StackResourceRecord(
    [property: JsonPropertyName("resource")] JsonElement Resource
);

public record StackResourcesResponse(
    [property: JsonPropertyName("resources")] StackResourceRecord[] Resources,
    [property: JsonPropertyName("region")] string? Region,
    [property: JsonPropertyName("version")] int Version
);

public record StackRenameRequest(
    [property: JsonPropertyName("newName")] string NewName,
    [property: JsonPropertyName("newProject")] string? NewProject
);

public record ExportStackResponse(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("features")] string[]? Features,
    [property: JsonPropertyName("deployment")] JsonElement Deployment
);

public record UntypedDeployment(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("features")] string[]? Features,
    [property: JsonPropertyName("deployment")] JsonElement Deployment
);

public record ImportStackResponse(
    [property: JsonPropertyName("updateId")] string UpdateId
);

public record GetUpdateEventsResponse(
    [property: JsonPropertyName("events")] EngineEvent[] Events,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);

public record UpdateViewInfo(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("endTime")] long? EndTime,
    [property: JsonPropertyName("updateID")] string? UpdateId
);

public record UpdateViewConfigEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("secret")] bool Secret,
    [property: JsonPropertyName("object")] bool Object
);

public record UpdateViewOutputEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("oldValue")] string? OldValue,
    [property: JsonPropertyName("newValue")] string? NewValue,
    [property: JsonPropertyName("changed")] bool Changed
);

public record UpdateViewResourceEntry(
    [property: JsonPropertyName("urn")] string? Urn,
    [property: JsonPropertyName("parentUrn")] string? ParentUrn,
    [property: JsonPropertyName("viewOf")] string? ViewOf,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("info")] string? Info,
    [property: JsonPropertyName("isError")] bool IsError
);

public record UpdateViewSummary(
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("maybeCorrupt")] bool MaybeCorrupt,
    [property: JsonPropertyName("durationSeconds")] long? DurationSeconds,
    [property: JsonPropertyName("resourceChanges")] Dictionary<string, int>? ResourceChanges
);

public record UpdateViewHeader(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("kindLabel")] string KindLabel,
    [property: JsonPropertyName("runLabel")] string RunLabel,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("endTime")] long? EndTime,
    [property: JsonPropertyName("resourceCount")] int? ResourceCount,
    [property: JsonPropertyName("resourceChanges")] Dictionary<string, int>? ResourceChanges
);

public record UpdateViewHistoryEntry(
    [property: JsonPropertyName("updateID")] string? UpdateId,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("kindLabel")] string KindLabel,
    [property: JsonPropertyName("runLabel")] string RunLabel,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("routeRunType")] string? RouteRunType,
    [property: JsonPropertyName("routeReference")] string? RouteReference,
    [property: JsonPropertyName("isSelected")] bool IsSelected
);

public record StackPageMetadata(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("activeUpdate")] string ActiveUpdate,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags
);

public record StackOverviewResponse(
    [property: JsonPropertyName("stack")] StackPageMetadata Stack,
    [property: JsonPropertyName("latest")] UpdateViewHeader? Latest,
    [property: JsonPropertyName("history")] UpdateViewHistoryEntry[] History
);

public record UpdateViewChangeConfigEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value
);

public record UpdateViewChangeEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("isError")] bool IsError,
    [property: JsonPropertyName("marker")] string? Marker = null,
    [property: JsonPropertyName("channel")] string? Channel = null,
    [property: JsonPropertyName("severity")] string? Severity = null,
    [property: JsonPropertyName("resourceUrn")] string? ResourceUrn = null,
    [property: JsonPropertyName("parentUrn")] string? ParentUrn = null,
    [property: JsonPropertyName("resourceType")] string? ResourceType = null,
    [property: JsonPropertyName("resourceName")] string? ResourceName = null,
    [property: JsonPropertyName("operation")] string? Operation = null,
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("info")] string? Info = null,
    [property: JsonPropertyName("text")] string? Text = null,
    [property: JsonPropertyName("summary")] string? Summary = null,
    [property: JsonPropertyName("configEntries")] UpdateViewChangeConfigEntry[]? ConfigEntries = null,
    [property: JsonPropertyName("rawPayloadJson")] string? RawPayloadJson = null
);

public record UpdateViewChanges(
    [property: JsonPropertyName("hasEvents")] bool HasEvents,
    [property: JsonPropertyName("messageCount")] int MessageCount,
    [property: JsonPropertyName("summaryText")] string SummaryText,
    [property: JsonPropertyName("diffText")] string DiffText,
    [property: JsonPropertyName("diagnosticText")] string DiagnosticText,
    [property: JsonPropertyName("entries")] UpdateViewChangeEntry[]? Entries = null,
    [property: JsonPropertyName("deltaEntries")] UpdateViewChangeEntry[]? DeltaEntries = null,
    [property: JsonPropertyName("summaryLines")] UpdateViewConsoleLine[]? SummaryLines = null,
    [property: JsonPropertyName("diffLines")] UpdateViewConsoleLine[]? DiffLines = null,
    [property: JsonPropertyName("diagnosticLines")] UpdateViewConsoleLine[]? DiagnosticLines = null,
    [property: JsonPropertyName("summaryLineDeltas")] UpdateViewConsoleLineDelta[]? SummaryLineDeltas = null,
    [property: JsonPropertyName("diffLineDeltas")] UpdateViewConsoleLineDelta[]? DiffLineDeltas = null,
    [property: JsonPropertyName("diagnosticLineDeltas")] UpdateViewConsoleLineDelta[]? DiagnosticLineDeltas = null
);

public record UpdateViewConsoleLine(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cssClass")] string CssClass,
    [property: JsonPropertyName("segments")] UpdateViewConsoleSegment[] Segments,
    [property: JsonPropertyName("cells")] UpdateViewConsoleCell[]? Cells = null
);

public record UpdateViewConsoleSegment(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cssClass")] string CssClass
);

public record UpdateViewConsoleCell(
    [property: JsonPropertyName("cssClass")] string CssClass,
    [property: JsonPropertyName("segments")] UpdateViewConsoleSegment[] Segments
);

public record UpdateViewConsoleLineDelta(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("anchorKey")] string? AnchorKey,
    [property: JsonPropertyName("line")] UpdateViewConsoleLine? Line
);

public record UpdateViewDelta(
    [property: JsonPropertyName("isIncremental")] bool IsIncremental,
    [property: JsonPropertyName("infoChanged")] bool InfoChanged,
    [property: JsonPropertyName("changesChanged")] bool ChangesChanged,
    [property: JsonPropertyName("configurationChanged")] bool ConfigurationChanged,
    [property: JsonPropertyName("outputsChanged")] bool OutputsChanged,
    [property: JsonPropertyName("resourcesChanged")] bool ResourcesChanged,
    [property: JsonPropertyName("summaryChanged")] bool SummaryChanged
);

public record UpdateViewResponse(
    [property: JsonPropertyName("stack")] StackPageMetadata Stack,
    [property: JsonPropertyName("info")] UpdateViewInfo Info,
    [property: JsonPropertyName("header")] UpdateViewHeader Header,
    [property: JsonPropertyName("history")] UpdateViewHistoryEntry[] History,
    [property: JsonPropertyName("configuration")] UpdateViewConfigEntry[] Configuration,
    [property: JsonPropertyName("outputs")] UpdateViewOutputEntry[] Outputs,
    [property: JsonPropertyName("resources")] UpdateViewResourceEntry[] Resources,
    [property: JsonPropertyName("summary")] UpdateViewSummary? Summary,
    [property: JsonPropertyName("changes")] UpdateViewChanges Changes,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken = null,
    [property: JsonPropertyName("delta")] UpdateViewDelta? Delta = null
);

public record EncryptValueRequest(
    [property: JsonPropertyName("plaintext")] byte[] Plaintext
);

public record EncryptValueResponse(
    [property: JsonPropertyName("ciphertext")] byte[] Ciphertext
);

public record DecryptValueRequest(
    [property: JsonPropertyName("ciphertext")] byte[] Ciphertext
);

public record DecryptValueResponse(
    [property: JsonPropertyName("plaintext")] byte[] Plaintext
);

public record BatchEncryptRequest(
    [property: JsonPropertyName("plaintexts")] byte[][] Plaintexts
);

public record BatchEncryptResponse(
    [property: JsonPropertyName("ciphertexts")] byte[][] Ciphertexts
);

public record BatchDecryptRequest(
    [property: JsonPropertyName("ciphertexts")] byte[][] Ciphertexts
);

public record BatchDecryptResponse(
    [property: JsonPropertyName("plaintexts")] Dictionary<string, byte[]> Plaintexts
);

public record ApiCapabilityConfig(
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("configuration")] JsonElement? Configuration
);

public record CapabilitiesResponse(
    [property: JsonPropertyName("capabilities")] ApiCapabilityConfig[] Capabilities
);

public record LogDecryptionEventRequest(
    [property: JsonPropertyName("secretName")] string? SecretName,
    [property: JsonPropertyName("commandName")] string? CommandName
);

public record CliVersionResponse(
    [property: JsonPropertyName("latestVersion")] string LatestVersion,
    [property: JsonPropertyName("oldestWithoutWarning")] string OldestWithoutWarning,
    [property: JsonPropertyName("latestDevVersion")] string? LatestDevVersion
);

public record Message(
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("message")] string Text
);

public record GetDefaultOrganizationResponse(
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    [property: JsonPropertyName("messages")] Message[] Messages
);

public record LogsResult(
    [property: JsonPropertyName("logs")] LogEntry[] Logs
);

public record LogEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("message")] string Message
);

public record UpdateContentsFilesResponse(
    [property: JsonPropertyName("files")] string[] Files
);

public record ListPolicyPacksResponse(
    [property: JsonPropertyName("policyPacks")] PolicyPackWithVersions[] PolicyPacks
);

public record PolicyPackWithVersions(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("versions")] int[] Versions,
    [property: JsonPropertyName("versionTags")] string[] VersionTags
);

public record ListPolicyGroupsResponse(
    [property: JsonPropertyName("policyGroups")] PolicyGroupSummary[] PolicyGroups
);

public record PolicyGroupSummary(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isOrgDefault")] bool IsOrgDefault,
    [property: JsonPropertyName("numStacks")] int NumStacks,
    [property: JsonPropertyName("numAccounts")] int? NumAccounts,
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("numEnabledPolicyPacks")] int NumEnabledPolicyPacks
);

public record CreatePolicyPackResponse(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("uploadURI")] string UploadUri,
    [property: JsonPropertyName("requiredHeaders")] Dictionary<string, string>? RequiredHeaders
);

public record PolicyPackConfigSchemaResponse(
    [property: JsonPropertyName("configSchema")] JsonElement? ConfigSchema
);

public record ListOrgTemplatesResponse(
    [property: JsonPropertyName("templates")] Dictionary<string, PulumiTemplateRemote[]> Templates,
    [property: JsonPropertyName("orgHasTemplates")] bool OrgHasTemplates,
    [property: JsonPropertyName("hasAccessError")] bool HasAccessError,
    [property: JsonPropertyName("hasUpstreamError")] bool HasUpstreamError
);

public record PulumiTemplateRemote(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sourceName")] string SourceName,
    [property: JsonPropertyName("sourceURL")] string SourceUrl,
    [property: JsonPropertyName("runtime")] string Runtime,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("quickstart")] string? Quickstart,
    [property: JsonPropertyName("config")] Dictionary<string, TemplateConfigValue>? Config,
    [property: JsonPropertyName("metadata")] Dictionary<string, string>? Metadata
);

public record TemplateConfigValue(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("default")] string? Default,
    [property: JsonPropertyName("secret")] bool? Secret
);

public record ListTemplatesResponse(
    [property: JsonPropertyName("templates")] TemplateMetadata[] Templates,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);

public record TemplateMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("readmeURL")] string ReadmeUrl,
    [property: JsonPropertyName("downloadURL")] string DownloadUrl,
    [property: JsonPropertyName("repoSlug")] string? RepoSlug,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("updatedAt")] string UpdatedAt,
    [property: JsonPropertyName("metadata")] Dictionary<string, string>? Metadata
);

public record StartTemplatePublishRequest(
    [property: JsonPropertyName("version")] string Version
);

public record StartTemplatePublishResponse(
    [property: JsonPropertyName("operationID")] string OperationId,
    [property: JsonPropertyName("uploadURLs")] TemplateUploadUrls UploadUrls
);

public record TemplateUploadUrls(
    [property: JsonPropertyName("archive")] string Archive
);

public record PublishTemplateVersionCompleteRequest(
    [property: JsonPropertyName("operationID")] string OperationId
);

public record StartPackagePublishRequest(
    [property: JsonPropertyName("version")] string Version
);

public record StartPackagePublishResponse(
    [property: JsonPropertyName("operationID")] string OperationId,
    [property: JsonPropertyName("uploadURLs")] PackageUploadUrls UploadUrls,
    [property: JsonPropertyName("requiredHeaders")] Dictionary<string, string>? RequiredHeaders
);

public record PackageUploadUrls(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("index")] string Index,
    [property: JsonPropertyName("installationConfiguration")] string InstallationConfiguration
);

public record CompletePackagePublishRequest(
    [property: JsonPropertyName("operationID")] string OperationId
);

public record ListPackagesResponse(
    [property: JsonPropertyName("packages")] PackageMetadata[] Packages,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);

public record PackageMetadata(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("logoUrl")] string? LogoUrl,
    [property: JsonPropertyName("repoUrl")] string? RepoUrl,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("isFeatured")] bool IsFeatured,
    [property: JsonPropertyName("packageTypes")] string[]? PackageTypes,
    [property: JsonPropertyName("packageStatus")] string? PackageStatus,
    [property: JsonPropertyName("readmeURL")] string ReadmeUrl,
    [property: JsonPropertyName("schemaURL")] string SchemaUrl,
    [property: JsonPropertyName("pluginDownloadURL")] string? PluginDownloadUrl,
    [property: JsonPropertyName("createdAt")] string CreatedAt,
    [property: JsonPropertyName("visibility")] string Visibility,
    [property: JsonPropertyName("parameterization")] JsonElement? Parameterization
);

public record GitHubAppIntegration(
    [property: JsonPropertyName("installed")] bool Installed
);

public record DeploymentSettingsRequest(
    [property: JsonPropertyName("tag")] string? Tag,
    [property: JsonPropertyName("executorContext")] JsonElement? ExecutorContext,
    [property: JsonPropertyName("sourceContext")] JsonElement? SourceContext,
    [property: JsonPropertyName("gitHub")] JsonElement? GitHub,
    [property: JsonPropertyName("operationContext")] JsonElement? OperationContext,
    [property: JsonPropertyName("agentPoolID")] string? AgentPoolId
);

public record CreateDeploymentRequest(
    [property: JsonPropertyName("operation")] JsonElement Operation,
    [property: JsonPropertyName("inheritSettings")] bool InheritSettings,
    [property: JsonPropertyName("executorContext")] JsonElement? ExecutorContext,
    [property: JsonPropertyName("sourceContext")] JsonElement? SourceContext,
    [property: JsonPropertyName("operationContext")] JsonElement? OperationContext,
    [property: JsonPropertyName("agentPoolID")] string? AgentPoolId
);

public record CreateDeploymentResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("consoleUrl")] string ConsoleUrl
);

public record DeploymentLogs(
    [property: JsonPropertyName("lines")] DeploymentLogLine[]? Lines,
    [property: JsonPropertyName("nextToken")] string? NextToken
);

public record DeploymentLogLine(
    [property: JsonPropertyName("header")] string? Header,
    [property: JsonPropertyName("timestamp")] string? Timestamp,
    [property: JsonPropertyName("line")] string? Line
);

public record SecretValueResponse(
    [property: JsonPropertyName("ciphertext")] string Ciphertext
);

public record TokenExchangeGrantResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("issued_token_type")] string IssuedTokenType,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] long ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken
);

public record PulumiQueryResponse(
    [property: JsonPropertyName("query")] string Query
);

public record ResourceSearchResponse(
    [property: JsonPropertyName("total")] long? Total,
    [property: JsonPropertyName("resources")] JsonElement? Resources,
    [property: JsonPropertyName("aggregations")] JsonElement? Aggregations,
    [property: JsonPropertyName("pagination")] JsonElement? Pagination,
    [property: JsonPropertyName("query")] string? Query
);
