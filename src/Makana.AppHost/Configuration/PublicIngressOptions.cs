namespace Makana.AppHost.Configuration;

internal sealed class PublicIngressOptions
{
    public const string SectionName = "PublicIngress";

    public string AppHost { get; init; } = "makana.foo";

    public string ApiHost { get; init; } = "api.makana.foo";

    public string AuthHost { get; init; } = "auth.makana.foo";

    public int? HttpsPort { get; init; }
}
