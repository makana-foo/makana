namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Represents a Litestream-managed database directory registration.
/// </summary>
/// <param name="name">The registration name.</param>
/// <param name="directoryPath">The database directory path.</param>
/// <param name="pattern">The glob pattern used to match database files.</param>
/// <param name="recursive">A value indicating whether subdirectories should be scanned recursively.</param>
/// <param name="watch">A value indicating whether directory watching is enabled.</param>
public sealed class LitestreamDatabaseGroupRegistration(
    string name,
    string directoryPath,
    string pattern,
    bool recursive,
    bool watch)
{
    /// <summary>
    /// Gets the registration name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the database directory path.
    /// </summary>
    public string DirectoryPath { get; } = directoryPath;

    /// <summary>
    /// Gets the glob pattern used to match database files.
    /// </summary>
    public string Pattern { get; } = pattern;

    /// <summary>
    /// Gets a value indicating whether subdirectories should be scanned recursively.
    /// </summary>
    public bool Recursive { get; } = recursive;

    /// <summary>
    /// Gets a value indicating whether directory watching is enabled.
    /// </summary>
    public bool Watch { get; } = watch;

    /// <summary>
    /// Resolves a database path relative to the configured directory.
    /// </summary>
    /// <param name="databaseName">The database file name or relative path.</param>
    /// <returns>The combined database path.</returns>
    public string ResolveDatabasePath(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        return Path.Combine(DirectoryPath, databaseName);
    }
}
