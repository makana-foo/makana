# CommunityToolkit.Aspire.Hosting.Litestream library

Provides extension methods and resource definitions for the Aspire AppHost to support configuring Litestream sidecars for SQLite replication.

The MVP package focuses on typed Litestream configuration for:

- a single SQLite database file
- a grouped or directory-based set of SQLite databases
- one replica target per Litestream resource

Current provider support includes:

- generic S3-compatible object storage
- MinIO
- Azure Blob Storage

## Getting Started

### Install the package

In your AppHost project, install the package using the following command:

```dotnetcli
dotnet add package CommunityToolkit.Aspire.Hosting.Litestream
```

### Example usage

Then, in the _Program.cs_ file of `AppHost`, define a Litestream resource:

```csharp
var litestream = builder.AddLitestream("litestream")
    .WithDataBindMount("./data/sqlite")
    .WithDatabase("app.db")
    .WithMinioReplica(minio, "sqlite-replicas", "apps/app");
```

To replicate a grouped directory of databases instead, configure a shared storage root and a directory scope:

```csharp
var litestream = builder.AddLitestream("litestream")
    .WithDataVolume()
    .WithDatabaseDirectory("tenants", pattern: "*.db", recursive: true, watch: true)
    .WithS3Replica(
        bucket: "sqlite-replicas",
        path: "apps/tenants",
        region: "us-east-1");
```

Use `WithDataVolume(...)` or `WithDataBindMount(...)` to place the SQLite files in a location that both your application and the Litestream sidecar can share.

## Development test harness

The repository includes a Docker-gated Aspire test harness for this integration under:

- `tests-app-hosts\CommunityToolkit.Aspire.Litestream.Testing.AppHost`
- `tests\CommunityToolkit.Aspire.Hosting.Litestream.Tests`

That harness provisions MinIO as the default S3-compatible target and validates the `add-litestream-integration` MVP with separate writer and verifier applications. Contributors should run it with the repository's existing .NET test-project pattern:

```dotnetcli
dotnet run --project .\tests\CommunityToolkit.Aspire.Hosting.Litestream.Tests\CommunityToolkit.Aspire.Hosting.Litestream.Tests.csproj --configuration Debug
```

Docker is required because the harness starts MinIO plus Litestream sidecars and also uses a one-off Litestream container for verifier restores.

### Test-only restore boundary

The verifier project's `/restore/*` endpoints exist only for harness recovery tests. They are intentionally scoped to `tests-app-hosts\CommunityToolkit.Aspire.Litestream.Testing.Verifier` so the public MVP integration surface remains limited to AppHost-side Litestream resource configuration rather than runtime restore helpers.

## Additional Information

https://github.com/CommunityToolkit/Aspire

## MVP scope and future expansion

This package currently models Litestream as a container-first sidecar with generated Litestream configuration.

The current MVP intentionally keeps the scope narrow:

- one configured provider per Litestream resource
- typed support for single-file and grouped-directory replication
- provider helpers for S3-compatible storage, MinIO, and Azure Blob Storage

Future work can expand this with additional Litestream providers, more advanced configuration options, and potentially provider-specific extension packages if that offers a cleaner API surface.

## Feedback & contributing

https://github.com/CommunityToolkit/Aspire
