var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.Makana_Api>("makana-api");

builder.AddProject<Projects.Makana_WebConsole>("makana-webconsole")
    .WithReference(api)
    .WithEnvironment("PULUMI_API_INTERNAL_ENDPOINT", api.GetEndpoint("https"));

builder.Build().Run();
