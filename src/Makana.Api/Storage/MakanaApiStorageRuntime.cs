using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

public sealed class MakanaApiStorageRuntime
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly object _statusLock = new();
    private StorageRuntimeStatus _status;

    public MakanaApiStorageRuntime(
        IConfiguration configuration,
        IHostEnvironment environment,
        IEnumerable<LitestreamDatabaseRegistration> databaseRegistrations,
        IEnumerable<LitestreamDatabaseGroupRegistration> databaseGroupRegistrations)
    {
        _configuration = configuration;
        _environment = environment;
        ControlDatabasePath = ResolveControlDatabasePath(configuration, environment, databaseRegistrations, databaseGroupRegistrations);
        DataRootPath = ResolveDataRootPath(ControlDatabasePath, databaseGroupRegistrations);
        ControlDatabaseConnectionString = $"Data Source={ControlDatabasePath}";
        _status = ComputeStatus();
    }

    public string ControlDatabasePath { get; }

    public string DataRootPath { get; }

    public string ControlDatabaseConnectionString { get; }

    public bool CanServeDatabaseTraffic => GetStatus().Availability is { RestoreState: StorageRestoreState.Ready, LeaseState: StorageLeaseState.Owner };

    public bool CanRunMaintenance => CanServeDatabaseTraffic;

    public bool HasLocalDatabaseState => GetStatus().LocalDatabaseState is LocalDatabaseState.Complete;

    public string UnavailableMessage => GetStatus().Availability.ToMessage();

    public void InitializeDatabase(SqliteControlDatabase database)
    {
        if (!GetStatus().Availability.CanInitializeDatabase)
        {
            return;
        }

        Directory.CreateDirectory(DataRootPath);
        database.Initialize();
        RefreshStatus();
    }

    public HealthCheckResult ToHealthCheckResult()
    {
        var status = GetStatus();
        if (CanServeDatabaseTraffic)
        {
            return HealthCheckResult.Healthy();
        }

        return status.Availability switch
        {
            { RestoreState: StorageRestoreState.Restoring } => HealthCheckResult.Degraded(status.Availability.ToMessage()),
            { LeaseState: StorageLeaseState.Standby } => HealthCheckResult.Degraded(status.Availability.ToMessage()),
            { LeaseState: StorageLeaseState.Reseeding } => HealthCheckResult.Degraded(status.Availability.ToMessage()),
            _ => HealthCheckResult.Unhealthy(status.Availability.ToMessage())
        };
    }

    private StorageRuntimeStatus GetStatus()
    {
        lock (_statusLock)
        {
            if (_status.LocalDatabaseState is LocalDatabaseState.Complete)
            {
                return _status;
            }

            _status = ComputeStatus();
            return _status;
        }
    }

    private void RefreshStatus()
    {
        lock (_statusLock)
        {
            _status = ComputeStatus();
        }
    }

    private StorageRuntimeStatus ComputeStatus()
    {
        var localDatabaseState = DetectLocalDatabaseState(ControlDatabasePath);
        var availability = StorageAvailabilityState.FromConfiguration(_configuration, _environment, localDatabaseState);
        return new StorageRuntimeStatus(localDatabaseState, availability);
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

    private static LocalDatabaseState DetectLocalDatabaseState(string controlDatabasePath)
    {
        if (!File.Exists(controlDatabasePath))
        {
            return LocalDatabaseState.None;
        }

        var controlDatabaseInfo = new FileInfo(controlDatabasePath);
        if (controlDatabaseInfo.Length == 0)
        {
            return LocalDatabaseState.None;
        }

        try
        {
            var database = new SqliteControlDatabase($"Data Source={controlDatabasePath}");
            var stackShardDatabase = new StackShardDatabase(database);

            using var connection = new SqliteConnection($"Data Source={controlDatabasePath};Mode=ReadOnly");
            connection.Open();

            if (!ControlDatabaseHasStacksTable(connection))
            {
                return LocalDatabaseState.Partial;
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT shard_path
                FROM stacks
                WHERE shard_path IS NOT NULL
                  AND shard_state = @activeState;
                """;
            command.Parameters.AddWithValue("@activeState", "active");

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!IsReadableShardDatabase(stackShardDatabase.ResolveCatalogShardPath(reader.GetString(0))))
                {
                    return LocalDatabaseState.Partial;
                }
            }

            return LocalDatabaseState.Complete;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            return controlDatabaseInfo.Length == 0
                ? LocalDatabaseState.None
                : LocalDatabaseState.Partial;
        }
    }

    private static bool ControlDatabaseHasStacksTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name = 'stacks'
            LIMIT 1;
            """;
        return command.ExecuteScalar() is not null;
    }

    private static bool IsReadableShardDatabase(string shardPath)
    {
        if (!File.Exists(shardPath))
        {
            return false;
        }

        try
        {
            using var connection = new SqliteConnection($"Data Source={shardPath};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('updates', 'deployments')
                GROUP BY 1
                HAVING COUNT(*) = 2;
                """;
            return command.ExecuteScalar() is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            return false;
        }
    }

    private readonly record struct StorageAvailabilityState(StorageRestoreState RestoreState, StorageLeaseState LeaseState)
    {
        public bool CanInitializeDatabase =>
            LeaseState is StorageLeaseState.Owner &&
            RestoreState is StorageRestoreState.Ready;

        public static StorageAvailabilityState FromConfiguration(
            IConfiguration configuration,
            IHostEnvironment environment,
            LocalDatabaseState localDatabaseState)
        {
            var replicaState = ParseReplicaState(configuration["Makana:Storage:ReplicaState"]);
            var restoreState = ParseRestoreState(configuration["Makana:Storage:RestoreState"]);
            var leaseState = ParseLeaseState(configuration["Makana:Storage:LeaseState"]);

            if (restoreState is null)
            {
                restoreState = ResolveRestoreState(environment, localDatabaseState, replicaState);
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
            LocalDatabaseState localDatabaseState,
            StorageReplicaState replicaState)
        {
            if (localDatabaseState is LocalDatabaseState.Complete)
            {
                return StorageRestoreState.Ready;
            }

            if (localDatabaseState is LocalDatabaseState.Partial)
            {
                return replicaState is StorageReplicaState.Absent
                    ? StorageRestoreState.Unavailable
                    : StorageRestoreState.Restoring;
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

    private enum LocalDatabaseState
    {
        None,
        Partial,
        Complete
    }

    private readonly record struct StorageRuntimeStatus(LocalDatabaseState LocalDatabaseState, StorageAvailabilityState Availability);

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
