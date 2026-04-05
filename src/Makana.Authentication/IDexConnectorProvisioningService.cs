namespace Makana.Authentication;

public interface IDexConnectorProvisioningService
{
    Task<OrganizationSsoProvisioningResult> UpsertOrganizationConnectorAsync(
        OrganizationSsoProvisioningRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteOrganizationConnectorAsync(string? connectionId, CancellationToken cancellationToken = default);
}
