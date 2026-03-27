using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

internal static class DexResourceBuilderExtensions
{
    public static IResourceBuilder<DexResource> AddDex(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        DexResource resource = new(name);

        return builder.AddResource(resource)
            .WithImage("dexidp/dex", "v2.45.1")
            .WithImageRegistry("ghcr.io")
            .WithBindMount(resource.ConfigurationFileHostPath, DexResource.ConfigurationFileTargetPath, isReadOnly: true)
            .WithArgs("/usr/local/bin/dex", "serve", DexResource.ConfigurationFileTargetPath);
    }
}
