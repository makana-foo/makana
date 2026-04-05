using System.Buffers;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Makana.Authentication.DexApi;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Makana.Authentication;

internal sealed class DexConnectorProvisioningService(
    IOptions<DexConnectorProvisioningOptions> options,
    IHostEnvironment environment) : IDexConnectorProvisioningService
{
    public async Task<OrganizationSsoProvisioningResult> UpsertOrganizationConnectorAsync(
        OrganizationSsoProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrganizationLogin);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RedirectUri);

        var connectionId = !string.IsNullOrWhiteSpace(request.ExistingConnectionId)
            ? request.ExistingConnectionId!
            : BuildConnectionId(request.OrganizationLogin, request.Protocol);

        if (TryCreateClient(out var client))
        {
            var connector = new Connector
            {
                Id = connectionId,
                Type = NormalizeProtocol(request.Protocol),
                Name = request.DisplayName,
                Config = BuildConnectorConfig(request)
            };

            var create = await client.CreateConnectorAsync(
                    new CreateConnectorReq { Connector = connector },
                    cancellationToken: cancellationToken)
                .ResponseAsync;
            if (create.AlreadyExists)
            {
                await client.UpdateConnectorAsync(
                        new UpdateConnectorReq
                        {
                            Id = connectionId,
                            NewType = connector.Type,
                            NewName = connector.Name,
                            NewConfig = connector.Config
                        },
                        cancellationToken: cancellationToken)
                    .ResponseAsync;
            }
        }
        else if (!environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Dex connector provisioning is unavailable because Dex:GrpcAddress is not configured.");
        }

        return new OrganizationSsoProvisioningResult(connectionId);
    }

    public async Task DeleteOrganizationConnectorAsync(string? connectionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            return;
        }

        if (TryCreateClient(out var client))
        {
            var response = await client.DeleteConnectorAsync(
                    new DeleteConnectorReq { Id = connectionId },
                    cancellationToken: cancellationToken)
                .ResponseAsync;
            if (response.NotFound)
            {
                return;
            }

            return;
        }

        if (!environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Dex connector provisioning is unavailable because Dex:GrpcAddress is not configured.");
        }
    }

    private bool TryCreateClient(out Dex.DexClient client)
    {
        if (string.IsNullOrWhiteSpace(options.Value.GrpcAddress))
        {
            client = null!;
            return false;
        }

        var channel = GrpcChannel.ForAddress(options.Value.GrpcAddress);
        client = new Dex.DexClient(channel);
        return true;
    }

    private static ByteString BuildConnectorConfig(OrganizationSsoProvisioningRequest request)
        => ByteString.CopyFromUtf8(BuildConnectorConfigJson(request));

    internal static string BuildConnectorConfigJson(OrganizationSsoProvisioningRequest request)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        switch (NormalizeProtocol(request.Protocol))
        {
            case "oidc":
                WriteOidcConfig(writer, request);
                break;
            case "saml":
                WriteSamlConfig(writer, request);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SSO protocol '{request.Protocol}'.");
        }

        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteOidcConfig(Utf8JsonWriter writer, OrganizationSsoProvisioningRequest request)
    {
        var oidc = request.Oidc ?? throw new InvalidOperationException("OIDC SSO requires OIDC identity provider settings.");
        writer.WriteStartObject();
        writer.WriteString("issuer", oidc.Issuer);
        writer.WriteString("clientID", oidc.ClientId);
        writer.WriteString("clientSecret", oidc.ClientSecret);
        writer.WriteString("redirectURI", request.RedirectUri);
        writer.WriteStartArray("scopes");
        writer.WriteStringValue("openid");
        writer.WriteStringValue("profile");
        writer.WriteStringValue("email");
        writer.WriteEndArray();
        writer.WriteBoolean("getUserInfo", true);
        writer.WriteEndObject();
    }

    private static void WriteSamlConfig(Utf8JsonWriter writer, OrganizationSsoProvisioningRequest request)
    {
        var saml = request.Saml ?? throw new InvalidOperationException("SAML SSO requires SAML identity provider settings.");
        writer.WriteStartObject();
        writer.WriteString("ssoURL", saml.SsoUrl);
        writer.WriteString("caData", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(saml.CaCertificatePem)));
        writer.WriteString("redirectURI", request.RedirectUri);
        writer.WriteString("usernameAttr", saml.UsernameAttribute);
        writer.WriteString("emailAttr", saml.EmailAttribute);
        WriteOptionalString(writer, "groupsAttr", saml.GroupsAttribute);
        WriteOptionalString(writer, "entityIssuer", saml.EntityIssuer);
        WriteOptionalString(writer, "ssoIssuer", saml.SsoIssuer);
        WriteOptionalString(writer, "groupsDelim", saml.GroupsDelimiter);
        WriteOptionalString(writer, "nameIDPolicyFormat", saml.NameIdPolicyFormat);
        writer.WriteEndObject();
    }

    private static string BuildConnectionId(string organizationLogin, string protocol)
        => $"org-{organizationLogin.Trim().ToLowerInvariant()}-{NormalizeProtocol(protocol)}";

    private static string NormalizeProtocol(string protocol)
        => string.Equals(protocol, "saml", StringComparison.OrdinalIgnoreCase) ? "saml" : "oidc";

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        var trimmed = NullIfWhiteSpace(value);
        if (trimmed is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, trimmed);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
