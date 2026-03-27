using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Provides extension methods for registering Litestream-managed database metadata in an <see cref="IHostApplicationBuilder"/>.
/// </summary>
public static class AspireLitestreamExtensions
{
    private const string DefaultDatabaseConfigSectionName = "Aspire:Litestream:Client:Database";
    private const string DefaultDatabaseGroupConfigSectionName = "Aspire:Litestream:Client:Group";

    /// <summary>
    /// Registers a single known Litestream-managed database in the application service collection.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">The registration name.</param>
    /// <param name="configureSettings">An optional callback used to customize the database settings after configuration binding.</param>
    public static void AddLitestreamDatabase(
        this IHostApplicationBuilder builder,
        string name,
        Action<LitestreamDatabaseSettings>? configureSettings = null) =>
            AddLitestreamDatabaseCore(builder, $"{DefaultDatabaseConfigSectionName}:{name}", name, configureSettings, serviceKey: null);

    /// <summary>
    /// Registers a single known Litestream-managed database as a keyed singleton in the application service collection.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">The registration name.</param>
    /// <param name="configureSettings">An optional callback used to customize the database settings after configuration binding.</param>
    public static void AddKeyedLitestreamDatabase(
        this IHostApplicationBuilder builder,
        string name,
        Action<LitestreamDatabaseSettings>? configureSettings = null) =>
            AddLitestreamDatabaseCore(builder, $"{DefaultDatabaseConfigSectionName}:{name}", name, configureSettings, serviceKey: name);

    /// <summary>
    /// Registers a Litestream-managed database directory scope in the application service collection.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">The registration name.</param>
    /// <param name="configureSettings">An optional callback used to customize the directory settings after configuration binding.</param>
    public static void AddLitestreamDatabaseGroup(
        this IHostApplicationBuilder builder,
        string name,
        Action<LitestreamDatabaseGroupSettings>? configureSettings = null) =>
            AddLitestreamDatabaseGroupCore(builder, $"{DefaultDatabaseGroupConfigSectionName}:{name}", name, configureSettings, serviceKey: null);

    /// <summary>
    /// Registers a keyed Litestream-managed database directory scope in the application service collection.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">The registration name.</param>
    /// <param name="configureSettings">An optional callback used to customize the directory settings after configuration binding.</param>
    public static void AddKeyedLitestreamDatabaseGroup(
        this IHostApplicationBuilder builder,
        string name,
        Action<LitestreamDatabaseGroupSettings>? configureSettings = null) =>
            AddLitestreamDatabaseGroupCore(builder, $"{DefaultDatabaseGroupConfigSectionName}:{name}", name, configureSettings, serviceKey: name);

    private static void AddLitestreamDatabaseCore(
        this IHostApplicationBuilder builder,
        string configurationSectionName,
        string name,
        Action<LitestreamDatabaseSettings>? configureSettings,
        object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var settings = LoadDatabaseSettings(builder.Configuration.GetSection(configurationSectionName));
        configureSettings?.Invoke(settings);

        ValidateDatabaseSettings(settings, name, configurationSectionName);

        LitestreamDatabaseRegistration registration = new(name, settings.DatabasePath!);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(registration);
        }
        else
        {
            builder.Services.AddKeyedSingleton(serviceKey, registration);
        }
    }

    private static void AddLitestreamDatabaseGroupCore(
        this IHostApplicationBuilder builder,
        string configurationSectionName,
        string name,
        Action<LitestreamDatabaseGroupSettings>? configureSettings,
        object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var settings = LoadDatabaseGroupSettings(builder.Configuration.GetSection(configurationSectionName));
        configureSettings?.Invoke(settings);

        ValidateDatabaseGroupSettings(settings, name, configurationSectionName);

        LitestreamDatabaseGroupRegistration registration = new(
            name,
            settings.DirectoryPath!,
            settings.Pattern,
            settings.Recursive,
            settings.Watch);

        if (serviceKey is null)
        {
            builder.Services.AddSingleton(registration);
        }
        else
        {
            builder.Services.AddKeyedSingleton(serviceKey, registration);
        }
    }

    private static void ValidateDatabaseSettings(LitestreamDatabaseSettings settings, string name, string configurationSectionName)
    {
        if (string.IsNullOrWhiteSpace(settings.DatabasePath))
        {
            throw new InvalidOperationException(
                $"Missing required {nameof(LitestreamDatabaseSettings.DatabasePath)} configuration for Litestream database '{name}' in section '{configurationSectionName}'.");
        }
    }

    private static void ValidateDatabaseGroupSettings(LitestreamDatabaseGroupSettings settings, string name, string configurationSectionName)
    {
        if (string.IsNullOrWhiteSpace(settings.DirectoryPath))
        {
            throw new InvalidOperationException(
                $"Missing required {nameof(LitestreamDatabaseGroupSettings.DirectoryPath)} configuration for Litestream database group '{name}' in section '{configurationSectionName}'.");
        }

        if (string.IsNullOrWhiteSpace(settings.Pattern))
        {
            throw new InvalidOperationException(
                $"Missing required {nameof(LitestreamDatabaseGroupSettings.Pattern)} configuration for Litestream database group '{name}' in section '{configurationSectionName}'.");
        }
    }

    private static LitestreamDatabaseSettings LoadDatabaseSettings(IConfigurationSection configurationSection) =>
        new()
        {
            DatabasePath = configurationSection[nameof(LitestreamDatabaseSettings.DatabasePath)],
        };

    private static LitestreamDatabaseGroupSettings LoadDatabaseGroupSettings(IConfigurationSection configurationSection) =>
        new()
        {
            DirectoryPath = configurationSection[nameof(LitestreamDatabaseGroupSettings.DirectoryPath)],
            Pattern = configurationSection[nameof(LitestreamDatabaseGroupSettings.Pattern)] ?? "*.db",
            Recursive = GetBooleanSetting(configurationSection, nameof(LitestreamDatabaseGroupSettings.Recursive)),
            Watch = GetBooleanSetting(configurationSection, nameof(LitestreamDatabaseGroupSettings.Watch)),
        };

    private static bool GetBooleanSetting(IConfigurationSection configurationSection, string key)
    {
        var value = configurationSection[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Invalid boolean configuration for Litestream setting '{configurationSection.Path}:{key}'. Expected 'true' or 'false' but found '{value}'.");
    }
}
