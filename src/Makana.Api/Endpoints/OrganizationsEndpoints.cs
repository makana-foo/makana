using System.Security.Cryptography;
using System.Text;
using Makana.Authentication;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class OrganizationsEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/orgs", (
            CreateOrganizationRequest request,
            AuthenticatedApiContextAccessor authenticatedApiContextAccessor,
            IdentityRepository identityRepository) =>
        {
            var caller = authenticatedApiContextAccessor.GetCurrent();
            if (string.IsNullOrWhiteSpace(caller.UserId))
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new ErrorResponse(400, "organization name is required", null));
            }

            var login = NormalizeOrganizationSlug(request.Name);
            if (string.IsNullOrWhiteSpace(login) || IsReservedOrganizationSlug(login))
            {
                return Results.BadRequest(new ErrorResponse(400, "organization name is invalid", null));
            }

            if (identityRepository.GetOrganizationByLogin(login) is not null ||
                string.Equals(identityRepository.GetUserIdByLogin(login), caller.UserId, StringComparison.Ordinal) ||
                identityRepository.GetUserIdByLogin(login) is not null)
            {
                return Results.Conflict(new ErrorResponse(409, "organization already exists", null));
            }

            try
            {
                var organization = identityRepository.CreateOrganization(request.Name, login, caller.UserId);
                return Results.Ok(new CreateOrganizationResponse(
                    organization.Id,
                    organization.Login,
                    organization.DisplayName,
                    $"/{organization.Login}/organizations"));
            }
            catch (InvalidOperationException)
            {
                return Results.Conflict(new ErrorResponse(409, "organization already exists", null));
            }
        });

        app.MapGet("/api/orgs/discover", (string email, IdentityRepository identityRepository) =>
        {
            var atIndex = email.LastIndexOf('@');
            if (atIndex < 0 || atIndex == email.Length - 1)
            {
                return Results.NotFound();
            }

            var organization = identityRepository.GetOrganizationByVerifiedEmailDomain(email[(atIndex + 1)..]);
            if (organization is null)
            {
                return Results.NotFound();
            }

            var sso = identityRepository.GetOrganizationSsoConfiguration(organization.Login);
            if (sso is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new OrganizationLoginDiscoveryResponse(
                organization.Login,
                sso.Protocol,
                sso.Mode,
                sso.ConnectionId,
                sso.DisplayName));
        }).AllowAnonymous();

        app.MapGet("/api/orgs/{org}/auth/login", (string org, IdentityRepository identityRepository) =>
        {
            var sso = identityRepository.GetOrganizationSsoConfiguration(org);
            if (sso is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new OrganizationLoginDiscoveryResponse(
                org,
                sso.Protocol,
                sso.Mode,
                sso.ConnectionId,
                sso.DisplayName));
        }).AllowAnonymous();

        app.MapPost("/api/orgs/{org}/membership/claim", (
            string org,
            AuthenticatedApiContextAccessor authenticatedApiContextAccessor,
            IdentityRepository identityRepository) =>
        {
            var caller = authenticatedApiContextAccessor.GetCurrent();
            if (string.IsNullOrWhiteSpace(caller.UserId))
            {
                return Results.Unauthorized();
            }

            var user = identityRepository.GetUserById(caller.UserId);
            if (user is null || string.IsNullOrWhiteSpace(user.Value.Email))
            {
                return Results.BadRequest(new ErrorResponse(400, "verified email is required to claim membership", null));
            }

            var claimed = identityRepository.ClaimOrganizationMembershipsForVerifiedEmail(caller.UserId, user.Value.Email!);
            if (!identityRepository.IsUserMemberOfOrgLogin(caller.UserId, org))
            {
                return Results.NotFound();
            }

            return Results.Ok(new ClaimOrganizationMembershipResponse(claimed));
        });

        var orgGroup = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        orgGroup.MapGet("/members", (string org, IdentityRepository identityRepository) =>
        {
            var members = identityRepository.ListOrganizationMembers(org)
                .Select(member => new OrganizationMemberResponse(
                    member.UserId,
                    member.Login,
                    member.DisplayName,
                    member.Email,
                    member.Role,
                    member.MembershipKind))
                .ToArray();
            return Results.Ok(new OrganizationMembersResponse(members));
        });

        var orgAdminGroup = orgGroup.MapGroup(string.Empty)
            .RequireAuthorization("OrgAdminRouteAccess");

        orgAdminGroup.MapPost("/invites", (string org, CreateOrganizationInviteRequest request, IdentityRepository identityRepository) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest(new ErrorResponse(400, "invite email is required", null));
            }

            if (identityRepository.GetOrganizationByLogin(org) is null)
            {
                return Results.NotFound();
            }

            var inviteId = identityRepository.CreateOrganizationInvite(org, request.Email, request.Role);
            return Results.Ok(new { id = inviteId });
        });

        orgAdminGroup.MapGet("/invites", (string org, IdentityRepository identityRepository) =>
        {
            var invites = identityRepository.ListOrganizationInvites(org)
                .Select(invite => new OrganizationInviteResponse(
                    invite.Id,
                    invite.Email,
                    invite.Role,
                    invite.CreatedAt.ToString("O"),
                    invite.Claimed,
                    invite.Revoked))
                .ToArray();
            return Results.Ok(new OrganizationInvitesResponse(invites));
        });

        orgAdminGroup.MapDelete("/invites/{inviteId}", (string org, string inviteId, IdentityRepository identityRepository) =>
        {
            return identityRepository.RevokeOrganizationInvite(org, inviteId)
                ? Results.NoContent()
                : Results.NotFound();
        });

        orgAdminGroup.MapPatch("/members/{userId}", (string org, string userId, UpdateOrganizationMemberRequest request, IdentityRepository identityRepository) =>
        {
            return identityRepository.SetOrganizationMemberRole(org, userId, request.Role)
                ? Results.NoContent()
                : Results.NotFound();
        });

        orgAdminGroup.MapDelete("/members/{userId}", (string org, string userId, IdentityRepository identityRepository) =>
        {
            return identityRepository.RemoveOrganizationMember(org, userId)
                ? Results.NoContent()
                : Results.NotFound();
        });

        orgAdminGroup.MapGet("/auth/sso", (string org, IdentityRepository identityRepository) =>
        {
            var config = identityRepository.GetOrganizationSsoConfiguration(org);
            if (config is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(ToOrganizationSsoResponse(config));
        });

        orgAdminGroup.MapPut("/auth/sso", async (
            string org,
            UpdateOrganizationSsoRequest request,
            IdentityRepository identityRepository,
            IDexConnectorProvisioningService dexConnectorProvisioningService,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var organization = identityRepository.GetOrganizationByLogin(org);
            if (organization is null)
            {
                return Results.NotFound();
            }

            var validationError = ValidateSsoRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new ErrorResponse(400, validationError, null));
            }

            var existing = identityRepository.GetOrganizationSsoConfiguration(org);
            var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? organization.DisplayName
                : request.DisplayName.Trim();

            var provisioned = await dexConnectorProvisioningService.UpsertOrganizationConnectorAsync(
                new OrganizationSsoProvisioningRequest(
                    organization.Login,
                    request.Protocol,
                    displayName,
                    BuildDexConnectorRedirectUri(configuration),
                    existing?.ConnectionId,
                    request.Oidc is null
                        ? null
                        : new OrganizationOidcIdentityProviderConfiguration(
                            request.Oidc.Issuer,
                            request.Oidc.ClientId,
                            request.Oidc.ClientSecret),
                    request.Saml is null
                        ? null
                        : new OrganizationSamlIdentityProviderConfiguration(
                            request.Saml.SsoUrl,
                            request.Saml.CaCertificatePem,
                            request.Saml.UsernameAttribute,
                            request.Saml.EmailAttribute,
                            request.Saml.GroupsAttribute,
                            request.Saml.EntityIssuer,
                            request.Saml.SsoIssuer,
                            request.Saml.GroupsDelimiter,
                            request.Saml.NameIdPolicyFormat)),
                cancellationToken);

            identityRepository.UpsertOrganizationSsoConfiguration(
                org,
                request.Protocol,
                request.Mode,
                provisioned.ConnectionId,
                displayName,
                request.ScimBearerToken,
                request.Domains ?? [],
                request.Oidc is null
                    ? null
                    : new OrganizationOidcConfigurationRecord(
                        request.Oidc.Issuer,
                        request.Oidc.ClientId,
                        request.Oidc.ClientSecret),
                request.Saml is null
                    ? null
                    : new OrganizationSamlConfigurationRecord(
                        request.Saml.SsoUrl,
                        request.Saml.CaCertificatePem,
                        request.Saml.UsernameAttribute,
                        request.Saml.EmailAttribute,
                        request.Saml.GroupsAttribute,
                        request.Saml.EntityIssuer,
                        request.Saml.SsoIssuer,
                        request.Saml.GroupsDelimiter,
                        request.Saml.NameIdPolicyFormat));
            var config = identityRepository.GetOrganizationSsoConfiguration(org)!;
            return Results.Ok(ToOrganizationSsoResponse(config));
        });

        orgAdminGroup.MapDelete("/auth/sso", async (
            string org,
            IdentityRepository identityRepository,
            IDexConnectorProvisioningService dexConnectorProvisioningService,
            CancellationToken cancellationToken) =>
        {
            var existing = identityRepository.GetOrganizationSsoConfiguration(org);
            if (existing is null)
            {
                return Results.NotFound();
            }

            await dexConnectorProvisioningService.DeleteOrganizationConnectorAsync(existing.ConnectionId, cancellationToken);
            return identityRepository.DeleteOrganizationSsoConfiguration(org)
                ? Results.NoContent()
                : Results.NotFound();
        });

        var scimGroup = app.MapGroup("/scim/v2/orgs/{org}")
            .AllowAnonymous();

        scimGroup.MapGet("/ServiceProviderConfig", (string org, HttpRequest request, IdentityRepository identityRepository) =>
        {
            if (!TryAuthorizeScimRequest(request, identityRepository, org, out _))
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new ScimServiceProviderConfigResponse(
                ["urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig"],
                new { supported = false },
                new { supported = false }));
        });

        scimGroup.MapPost("/Users", (string org, HttpRequest request, ScimUserRequest payload, IdentityRepository identityRepository) =>
        {
            if (!TryAuthorizeScimRequest(request, identityRepository, org, out _))
            {
                return Results.Unauthorized();
            }

            var email = ResolveScimEmail(payload);
            if (string.IsNullOrWhiteSpace(payload.ExternalId) || string.IsNullOrWhiteSpace(email))
            {
                return Results.BadRequest(new ErrorResponse(400, "externalId and email are required", null));
            }

            identityRepository.UpsertScimProvisionedMembership(
                org,
                payload.ExternalId,
                email!,
                payload.DisplayName ?? payload.UserName,
                "member",
                payload.Active ?? true);

            return Results.Ok(new ScimUserResponse(
                payload.ExternalId,
                payload.ExternalId,
                payload.UserName,
                payload.DisplayName,
                payload.Active ?? true));
        });

        scimGroup.MapPut("/Users/{externalId}", (string org, string externalId, HttpRequest request, ScimUserRequest payload, IdentityRepository identityRepository) =>
        {
            if (!TryAuthorizeScimRequest(request, identityRepository, org, out _))
            {
                return Results.Unauthorized();
            }

            var email = ResolveScimEmail(payload);
            if (string.IsNullOrWhiteSpace(email))
            {
                return Results.BadRequest(new ErrorResponse(400, "email is required", null));
            }

            identityRepository.UpsertScimProvisionedMembership(
                org,
                externalId,
                email!,
                payload.DisplayName ?? payload.UserName,
                "member",
                payload.Active ?? true);

            return Results.Ok(new ScimUserResponse(
                externalId,
                externalId,
                payload.UserName,
                payload.DisplayName,
                payload.Active ?? true));
        });

        scimGroup.MapDelete("/Users/{externalId}", (string org, string externalId, HttpRequest request, IdentityRepository identityRepository) =>
        {
            if (!TryAuthorizeScimRequest(request, identityRepository, org, out _))
            {
                return Results.Unauthorized();
            }

            return identityRepository.DeactivateScimProvisionedMembership(org, externalId)
                ? Results.NoContent()
                : Results.NotFound();
        });

        return app;
    }

    private static bool TryAuthorizeScimRequest(
        HttpRequest request,
        IdentityRepository identityRepository,
        string org,
        out OrganizationSsoConfigurationRecord? configuration)
    {
        configuration = null;
        if (!request.Headers.TryGetValue("Authorization", out var headerValue))
        {
            return false;
        }

        var raw = headerValue.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var bearer = raw["Bearer ".Length..].Trim();
        configuration = identityRepository.GetOrganizationSsoConfiguration(org);
        return configuration is not null &&
            !string.IsNullOrWhiteSpace(configuration.ScimBearerToken) &&
            string.Equals(configuration.ScimBearerToken, bearer, StringComparison.Ordinal);
    }

    private static string? ResolveScimEmail(ScimUserRequest payload)
    {
        var primary = payload.Emails?.FirstOrDefault(static email => email.Primary == true)?.Value;
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        var first = payload.Emails?.FirstOrDefault()?.Value;
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first;
        }

        return payload.UserName.Contains('@', StringComparison.Ordinal)
            ? payload.UserName
            : null;
    }

    private static OrganizationSsoResponse ToOrganizationSsoResponse(OrganizationSsoConfigurationRecord config)
        => new(
            config.Protocol,
            config.Mode,
            config.ConnectionId,
            config.DisplayName,
            [.. config.Domains],
            !string.IsNullOrWhiteSpace(config.ScimBearerToken),
            config.ScimBearerToken,
            config.Oidc is null
                ? null
                : new OrganizationOidcConfigurationRequest(
                    config.Oidc.Issuer,
                    config.Oidc.ClientId,
                    config.Oidc.ClientSecret),
            config.Saml is null
                ? null
                : new OrganizationSamlConfigurationRequest(
                    config.Saml.SsoUrl,
                    config.Saml.CaCertificatePem,
                    config.Saml.UsernameAttribute,
                    config.Saml.EmailAttribute,
                    config.Saml.GroupsAttribute,
                    config.Saml.EntityIssuer,
                    config.Saml.SsoIssuer,
                    config.Saml.GroupsDelimiter,
                    config.Saml.NameIdPolicyFormat));

    private static string BuildDexConnectorRedirectUri(IConfiguration configuration)
    {
        var authority = configuration["OIDC_AUTHORITY"];
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidOperationException("OIDC_AUTHORITY must be configured to provision organization SSO.");
        }

        return new Uri(new Uri(authority.TrimEnd('/'), UriKind.Absolute), "/callback").ToString();
    }

    private static string? ValidateSsoRequest(UpdateOrganizationSsoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Protocol))
        {
            return "SSO protocol is required.";
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return "SSO display name is required.";
        }

        if (string.Equals(request.Protocol, "saml", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Saml is null)
            {
                return "SAML SSO settings are required.";
            }

            if (string.IsNullOrWhiteSpace(request.Saml.SsoUrl) ||
                string.IsNullOrWhiteSpace(request.Saml.CaCertificatePem) ||
                string.IsNullOrWhiteSpace(request.Saml.UsernameAttribute) ||
                string.IsNullOrWhiteSpace(request.Saml.EmailAttribute))
            {
                return "SAML SSO requires an SSO URL, CA certificate, username attribute, and email attribute.";
            }

            return null;
        }

        if (request.Oidc is null)
        {
            return "OIDC SSO settings are required.";
        }

        return string.IsNullOrWhiteSpace(request.Oidc.Issuer) ||
               string.IsNullOrWhiteSpace(request.Oidc.ClientId) ||
               string.IsNullOrWhiteSpace(request.Oidc.ClientSecret)
            ? "OIDC SSO requires an issuer URL, client ID, and client secret."
            : null;
    }

    private static string NormalizeOrganizationSlug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var rune in value.Trim().ToLowerInvariant())
        {
            if ((rune >= 'a' && rune <= 'z') || (rune >= '0' && rune <= '9'))
            {
                builder.Append(rune);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
            {
                continue;
            }

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    private static bool IsReservedOrganizationSlug(string slug)
        => slug is "api" or "login" or "logout" or "account" or "organizations" or "stacks" or "scim";
}
