namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Represents a single known Litestream-managed database registration.
/// </summary>
/// <param name="name">The registration name.</param>
/// <param name="databasePath">The database path.</param>
public sealed class LitestreamDatabaseRegistration(string name, string databasePath)
{
    /// <summary>
    /// Gets the registration name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the database path.
    /// </summary>
    public string DatabasePath { get; } = databasePath;
}
