namespace Makana.AppHost.Configuration;

internal sealed class ApiContainerOptions
{
    public const string SectionName = "ApiContainer";
    private const string DefaultAotFinalBaseImage = "mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled";
    private const string DefaultFrameworkDependentFinalBaseImage = "mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra";

    public bool PublishAot { get; init; } = true;
    public string AotFinalBaseImage { get; init; } = DefaultAotFinalBaseImage;
    public string FrameworkDependentFinalBaseImage { get; init; } = DefaultFrameworkDependentFinalBaseImage;

    public string ResolveFinalBaseImage()
    {
        Validate();

        return PublishAot
            ? AotFinalBaseImage
            : FrameworkDependentFinalBaseImage;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AotFinalBaseImage))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(AotFinalBaseImage)} must be configured.");
        }

        if (string.IsNullOrWhiteSpace(FrameworkDependentFinalBaseImage))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(FrameworkDependentFinalBaseImage)} must be configured.");
        }
    }
}
