# CommunityToolkit.Aspire.Litestream library

Register Litestream-managed database metadata in the DI container so applications can compose Litestream configuration with their existing SQLite libraries.

This package does not replace `Microsoft.Data.Sqlite` or EF Core SQLite integrations. Instead, it gives your application a typed way to discover the database file or database directory that Litestream is managing.

## Getting Started

### Prerequisites

-   A Litestream-managed SQLite database or database directory scope

### Install the package

Install the Litestream client library using the following command:

```dotnetcli
dotnet add package CommunityToolkit.Aspire.Litestream
```

### Example usage

In the _Program.cs_ file of your project, call the `AddLitestreamDatabase` extension method to register a single known database:

```csharp
builder.AddLitestreamDatabase("sqlite");
```

Or register a grouped directory scope for tenant databases:

```csharp
builder.AddLitestreamDatabaseGroup("tenants");
```

The client package binds from configuration under:

- `Aspire:Litestream:Client:Database:{name}`
- `Aspire:Litestream:Client:Group:{name}`

For example:

```json
{
  "Aspire": {
    "Litestream": {
      "Client": {
        "Database": {
          "sqlite": {
            "DatabasePath": "/data/app.db"
          }
        },
        "Group": {
          "tenants": {
            "DirectoryPath": "/data/tenants",
            "Pattern": "*.db",
            "Recursive": true,
            "Watch": true
          }
        }
      }
    }
  }
}
```

After registration, resolve `LitestreamDatabaseRegistration` or `LitestreamDatabaseGroupRegistration` from DI and use the returned paths to configure your preferred SQLite client library.

## Additional documentation

-   https://github.com/CommunityToolkit/Aspire

## Feedback & contributing

https://github.com/CommunityToolkit/Aspire
