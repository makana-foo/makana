namespace Makana.Api.Updates;

internal interface IMakanaApiMaintenanceRunner
{
    Task RunMaintenanceAsync(CancellationToken cancellationToken);
}
