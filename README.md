# Makana

Makana is an Aspire-orchestrated reimplementation of the Pulumi Cloud backend and web console behavior required by the Pulumi CLI. The main runtime lives in `src/Makana.AppHost`, which composes the API, Razor-based web console, public ingress, built-in OIDC resources, and Litestream replication helpers.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/` | Application projects, including `Makana.Api`, `Makana.WebConsole`, and `Makana.AppHost`. |
| `test/` | xUnit, Aspire, and Playwright test projects. |
| `integration/` | Sample Pulumi programs used for end-to-end validation. |
| `info/` | Numbered implementation notes and change history. |

## Local development

```bash
dotnet build Makana.slnx -v minimal
dotnet test --project test/Makana.Api.Tests/Makana.Api.Tests.csproj -v minimal --no-restore
dotnet test --project test/Makana.WebConsole.Tests/Makana.WebConsole.Tests.csproj -v minimal --no-restore
aspire start --isolated
```

## Kubernetes deployment

Makana's AppHost already references `Aspire.Hosting.Kubernetes` and now registers a Kubernetes environment, so `aspire publish` emits a Helm chart for the full application model. The deployment flow follows the current Aspire guidance: generate Kubernetes artifacts with `aspire publish`, provide environment-specific values, then deploy with Helm.

### Prerequisites

1. .NET 10 SDK
2. [Aspire CLI](https://aspire.dev/install)
3. Docker or another OCI image build pipeline
4. `kubectl` configured for the target cluster
5. Helm 3
6. A container registry reachable from the cluster

### 1. Provide production configuration before publishing

`aspire publish` captures the AppHost configuration that is active at publish time. Set production values in environment variables, user secrets, or an `appsettings.Production.json` file before generating artifacts.

These settings are the important ones for Makana:

| Setting | Why it matters |
| --- | --- |
| `PublicIngress__AppHost`, `PublicIngress__ApiHost`, `PublicIngress__AuthHost` | Publishes the external hostnames used by the YARP ingress and generated public URLs. |
| `Parameters__oidc_provider`, `Parameters__oidc_mode`, `Parameters__oidc_client_id`, `Parameters__oidc_client_secret` | Controls whether Makana publishes the built-in OIDC resources or points at an external provider. |
| `Parameters__pocketid_encryption_key`, `Parameters__pocketid_static_api_key` | Required when the built-in Pocket ID resources are enabled. |
| `Parameters__makana_api_db_path` | Sets the SQLite control database path inside the published deployment. |
| `Parameters__makana_litestream_*`, `Parameters__pocketid_litestream_*` | Configures S3-compatible Litestream replication for API and OIDC state. |
| `ApiData__UsePersistentState`, `PocketId__UsePersistentState`, `Dex__UsePersistentState` | Controls whether the published deployment uses durable storage-related resources. |

For Aspire parameters that contain dashes in AppHost code, use underscores in environment variables. For example, `Parameters__oidc_provider` maps to the AppHost parameter named `oidc-provider`.

Example:

```bash
export PublicIngress__AppHost=makana.example.com
export PublicIngress__ApiHost=api.makana.example.com
export PublicIngress__AuthHost=auth.makana.example.com

export Parameters__oidc_provider=dex
export Parameters__oidc_mode=local
export Parameters__oidc_client_id=makana-web
export Parameters__pocketid_encryption_key=replace-me
export Parameters__pocketid_static_api_key=replace-me

export Parameters__makana_litestream_bucket=makana-prod
export Parameters__makana_litestream_path=makana/api
export Parameters__makana_litestream_region=us-east-1
export Parameters__makana_litestream_endpoint=https://s3.example.com
export Parameters__makana_litestream_access_key=replace-me
export Parameters__makana_litestream_secret_key=replace-me
```

### 2. Generate Kubernetes artifacts

```bash
aspire publish --project src/Makana.AppHost/Makana.AppHost.csproj -o artifacts/k8s
```

The output directory contains a Helm chart (`Chart.yaml`, `values.yaml`, `templates/`) plus the generated container build contexts needed by the published services.

### 3. Set image references and secret overrides

The generated `artifacts/k8s/values.yaml` is the authoritative map of publish-time values. For Makana's default built-in OIDC setup it includes parameterized images for:

- `parameters.api.api_image`
- `parameters.app.app_image`
- `parameters.makana_api_litestream.makana_api_litestream_image`
- `parameters.oidc_dex.oidc_dex_image`
- `parameters.oidc_litestream.oidc_litestream_image`

Keep environment-specific overrides in a separate values file instead of editing the generated chart in place:

```yaml
parameters:
  api:
    api_image: ghcr.io/your-org/makana-api:2026-04-16
  app:
    app_image: ghcr.io/your-org/makana-app:2026-04-16
  makana_api_litestream:
    makana_api_litestream_image: ghcr.io/your-org/makana-api-litestream:2026-04-16
  oidc_dex:
    oidc_dex_image: ghcr.io/your-org/makana-oidc-dex:2026-04-16
  oidc_litestream:
    oidc_litestream_image: ghcr.io/your-org/makana-oidc-litestream:2026-04-16
```

If your publish-time configuration enables or disables different resources, regenerate the chart and use the new `values.yaml` as the source of truth for the matching override keys.

### 4. Deploy with Helm

```bash
helm upgrade --install makana ./artifacts/k8s \
  --namespace makana \
  --create-namespace \
  -f values.production.yaml
```

### 5. Expose and verify the deployment

The generated `public-ingress-service` is a `ClusterIP` service that exposes:

- port `5000` for HTTP
- port `8000` for HTTPS

Front that service with your preferred Ingress or LoadBalancer configuration, then verify the release:

```bash
helm status makana -n makana
kubectl get pods,svc -n makana
kubectl logs deployment/public-ingress-deployment -n makana
```

## Reference

- [Aspire Kubernetes integration](https://aspire.dev/integrations/compute/kubernetes/)
- [Aspire: Deploy to Kubernetes](https://aspire.dev/deployment/kubernetes/)
