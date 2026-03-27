namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Represents the settings for a grouped Litestream-managed database directory.
/// </summary>
public sealed class LitestreamDatabaseGroupSettings
{
    /// <summary>
    /// Gets or sets the directory that contains the application's SQLite database files.
    /// </summary>
    public string? DirectoryPath { get; set; }

    /// <summary>
    /// Gets or sets the glob pattern used to match SQLite database files.
    /// </summary>
    public string Pattern { get; set; } = "*.db";

    /// <summary>
    /// Gets or sets a value indicating whether subdirectories should be scanned recursively.
    /// </summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Litestream-style directory watching is enabled.
    /// </summary>
    public bool Watch { get; set; }
}
