using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

internal static class DexResourceBuilderExtensions
{
    private const string DexSourceRef = "410a58f5ac7769f2ab9b98deabce323a93a8c73a";

    public static IResourceBuilder<DexResource> AddDex(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string configurationFilePath = DexResource.ConfigurationFileTargetPath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFilePath);

        DexResource resource = new(name);
        var finalBaseImage = builder.ExecutionContext.IsPublishMode
            ? "ghcr.io/dexidp/dex:latest-distroless"
            : "ghcr.io/dexidp/dex:latest-alpine";

        return builder.AddResource(resource)
            .WithImage("makana/dex", "patched")
            .WithDockerfile("../Dex")
            .WithBuildArg("DEX_REF", DexSourceRef)
            .WithBuildArg("FINAL_BASE_IMAGE", finalBaseImage)
            .WithArgs("/usr/local/bin/dex", "serve", configurationFilePath);
    }
}
