using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Makana.Api.Storage;

public sealed class MakanaApiStorageRuntime
{
    private readonly StorageAvailabilityState _availability;
    private readonly bool _hasLocalDatabaseState;

    public MakanaApiStorageRuntime(
        IConfiguration configuration,
        IHostEnvironment environment,
        IEnumerable<LitestreamDatabaseRegistration> databaseRegistrations,
        IEnumerable<LitestreamDatabaseGroupRegistration> databaseGroupRegistrations)
    {
        ControlDatabasePath = ResolveControlDatabasePath(configuration, environment, databaseRegistrations, databaseGroupRegistrations);
        DataRootPath = ResolveDataRootPath(ControlDatabasePath, databaseGroupRegistrations);
        ControlDatabaseConnectionString = $"Data Source={ControlDatabasePath}";
        _hasLocalDatabaseState = DetectLocalDatabaseState(DataRootPath, ControlDatabasePath);
        _availability = StorageAvailabilityState.FromConfiguration(configuration, environment, _hasLocalDatabaseState);
    }

    public string ControlDatabasePath { get; }

    public string DataRootPath { get; }

    public string ControlDatabaseConnectionString { get; }

    public bool CanServeDatabaseTraffic => _availability is { RestoreState: StorageRestoreState.Ready, LeaseState: StorageLeaseState.Owner };

    public bool CanRunMaintenance => CanServeDatabaseTraffic;

    public bool HasLocalDatabaseState => _hasLocalDatabaseState;

    public string UnavailableMessage => _availability.ToMessage();

    public void InitializeDatabase(SqliteControlDatabase database)
    {
        if (!_availability.CanInitializeDatabase)
        {
            return;
        }

        Directory.CreateDirectory(DataRootPath);
        database.Initialize();
    }

    public HealthCheckResult ToHealthCheckResult()
    {
        if (CanServeDatabaseTraffic)
        {
            return HealthCheckResult.Healthy();
        }

        return _availability switch
        {
            { RestoreState: StorageRestoreState.Restoring } => HealthCheckResult.Degraded(UnavailableMessage),
            { LeaseState: StorageLeaseState.Standby } => HealthCheckResult.Degraded(UnavailableMessage),
            { LeaseState: StorageLeaseState.Reseeding } => HealthCheckResult.Degraded(UnavailableMessage),
            _ => HealthCheckResult.Unhealthy(UnavailableMessage)
        };
    }

    private static string ResolveControlDatabasePath(
        IConfiguration configuration,
        IHostEnvironment environment,
        IEnumerable<LitestreamDatabaseRegistration> databaseRegistrations,
        IEnumerable<LitestreamDatabaseGroupRegistration> databaseGroupRegistrations)
    {
        var litestreamDatabase = databaseRegistrations.FirstOrDefault(static registration =>
            string.Equals(registration.Name, MakanaLitestreamNames.ControlDatabase, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(litestreamDatabase?.DatabasePath))
        {
            return Path.GetFullPath(litestreamDatabase.DatabasePath);
        }

        var litestreamGroup = databaseGroupRegistrations.FirstOrDefault(static registration =>
            string.Equals(registration.Name, MakanaLitestreamNames.ApiDataRoot, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(litestreamGroup?.DirectoryPath))
        {
            return Path.GetFullPath(litestreamGroup.ResolveDatabasePath("makana.db"));
        }

        var directPath = configuration["DOTNET_API_DB"];
        if (string.IsNullOrWhiteSpace(directPath))
        {
            throw new InvalidOperationException(
                "Missing Makana API database configuration. Configure Aspire Litestream client metadata or DOTNET_API_DB in Development/Testing.");
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("DOTNET_API_DB is only supported in Development and Testing.");
        }

        return Path.GetFullPath(directPath);
    }

    private static string ResolveDataRootPath(
        string controlDatabasePath,
        IEnumerable<LitestreamDatabaseGroupRegistration> databaseGroupRegistrations)
    {
        var litestreamGroup = databaseGroupRegistrations.FirstOrDefault(static registration =>
            string.Equals(registration.Name, MakanaLitestreamNames.ApiDataRoot, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(litestreamGroup?.DirectoryPath))
        {
            return Path.GetFullPath(litestreamGroup.DirectoryPath);
        }

        return Path.GetDirectoryName(controlDatabasePath)
            ?? throw new InvalidOperationException("Unable to resolve the Makana API data root.");
    }

    private static bool DetectLocalDatabaseState(string dataRootPath, string controlDatabasePath)
    {
        _ = dataRootPath;
        return File.Exists(controlDatabasePath);
    }

    private readonly record struct StorageAvailabilityState(StorageRestoreState RestoreState, StorageLeaseState LeaseState)
    {
        public bool CanInitializeDatabase =>
            LeaseState is StorageLeaseState.Owner &&
            RestoreState is StorageRestoreState.Ready;

        public static StorageAvailabilityState FromConfiguration(
            IConfiguration configuration,
            IHostEnvironment environment,
            bool hasLocalDatabaseState)
        {
            var replicaState = ParseReplicaState(configuration["Makana:Storage:ReplicaState"]);
            var restoreState = ParseRestoreState(configuration["Makana:Storage:RestoreState"]);
            var leaseState = ParseLeaseState(configuration["Makana:Storage:LeaseState"]);

            if (restoreState is null)
            {
                restoreState = ResolveRestoreState(environment, hasLocalDatabaseState, replicaState);
            }

            return new StorageAvailabilityState(restoreState.Value, leaseState);
        }

        public string ToMessage()
        {
            if (RestoreState is StorageRestoreState.Restoring)
            {
                return "Makana API storage restore is in progress.";
            }

            return LeaseState switch
            {
                StorageLeaseState.Standby => "Makana API is running in standby mode and is not serving traffic.",
                StorageLeaseState.Reseeding => "Makana API is reseeding from the promoted writer and is not serving traffic.",
                StorageLeaseState.Unavailable => "Makana API has no writable database lease available.",
                _ when RestoreState is StorageRestoreState.Unavailable => "Makana API has no local data and no bootstrap or restore decision is available.",
                _ => "Makana API storage is unavailable."
            };
        }

        private static StorageRestoreState ResolveRestoreState(
            IHostEnvironment environment,
            bool hasLocalDatabaseState,
            StorageReplicaState replicaState)
        {
            if (hasLocalDatabaseState)
            {
                return StorageRestoreState.Ready;
            }

            return replicaState switch
            {
                StorageReplicaState.Absent => StorageRestoreState.Ready,
                StorageReplicaState.Available => StorageRestoreState.Restoring,
                _ when environment.IsDevelopment() || environment.IsEnvironment("Testing") => StorageRestoreState.Ready,
                _ => StorageRestoreState.Unavailable
            };
        }

        private static StorageRestoreState? ParseRestoreState(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "restoring" => StorageRestoreState.Restoring,
                "unavailable" => StorageRestoreState.Unavailable,
                "ready" => StorageRestoreState.Ready,
                null or "" => null,
                _ => StorageRestoreState.Ready
            };
        }

        private static StorageLeaseState ParseLeaseState(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "standby" => StorageLeaseState.Standby,
                "reseeding" => StorageLeaseState.Reseeding,
                "unavailable" => StorageLeaseState.Unavailable,
                _ => StorageLeaseState.Owner
            };
        }

        private static StorageReplicaState ParseReplicaState(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "absent" => StorageReplicaState.Absent,
                "available" => StorageReplicaState.Available,
                _ => StorageReplicaState.Unknown
            };
        }
    }

    private enum StorageRestoreState
    {
        Ready,
        Restoring,
        Unavailable
    }

    private enum StorageLeaseState
    {
        Owner,
        Standby,
        Reseeding,
        Unavailable
    }

    private enum StorageReplicaState
    {
        Unknown,
        Absent,
        Available
    }
}
