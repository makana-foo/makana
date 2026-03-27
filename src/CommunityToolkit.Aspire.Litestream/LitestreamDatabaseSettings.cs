namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Represents the settings for a single known Litestream-managed database.
/// </summary>
public sealed class LitestreamDatabaseSettings
{
    /// <summary>
    /// Gets or sets the local database path that the application should use.
    /// </summary>
    public string? DatabasePath { get; set; }
}
