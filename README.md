# SAML Sample App

Sample ASP.NET Core MVC application with:

- User-facing site (`role=user`) that displays SAML token claims.
- Admin portal (`role=admin`) for SAML IdP configuration and certificate lifecycle.
- API endpoints for certificate management protected by machine credential (Basic auth).

## Local run

1. `cd "Saml Sample App/SamlSample.Web"`
2. `dotnet restore`
3. `dotnet run`
4. Open `https://localhost:5001` (or printed URL).

On first run, API credentials are generated and saved to:

- `.internal/bootstrap-api-credentials.txt`

For first-time admin setup before SAML is configured, local bootstrap admin credentials are also generated:

- URL: `/bootstrap-admin/login`
- File: `.internal/bootstrap-admin-credentials.txt`

The bootstrap admin login is available only for local requests in Development. Disable it in the admin portal after onboarding SAML.

## API endpoints

- `GET /v1/idp-connections/{connectionId}`
- `POST /v1/idp-connections/{connectionId}/rotation/discover`
- `POST /v1/idp-connections/{connectionId}/rotation/activate`
- `GET /v1/idp-connections/{connectionId}/logs`

Use API key auth via `X-API-Key: <key>` or `Authorization: ApiKey <key>`.

## API keys

- Admin page: `/Admin/Apis`
- Generate API key with optional label.
- The secret is shown one time only and is never stored in plaintext.
- Store only in secure secret manager and rotate keys periodically.

## Admin portal

- Path: `/Admin/Sso`
- Configure SP/IdP values and metadata URL.
- Import Entra federation metadata URL to auto-load signing certs.
- Set one cert as primary.
- Delete secondary certs.
- Left admin tray includes `SSO Configuration` and `Logs`.
- Logs page (`/Admin/Logs`) records before/after configuration changes and supports date sorting.
- App controls page (`/Admin/AppControls`) includes `Log retention (days)`, `Daily run time (UTC)`, and read-only `Next scheduled run`.

## Automatic certificate refresh

- The app runs an in-process background job daily at the configured UTC run time to refresh metadata signing certificates.
- The same daily run enforces log retention and purges old log entries.
- This works as long as the app process is running.
- In Azure App Service, enable **Always On** to keep this process active.
- For stronger operational guarantees across scale-out or sleeping instances, use an external scheduler such as Azure Functions Timer or WebJobs.

## Production guidance

- Store API credentials in Azure Key Vault or replace with Entra-issued tokens for API access.
- Use Azure App Service + Azure SQL + Key Vault.
- Enforce HTTPS only and restrict app registration redirect URLs.

## Control-plane design docs

- `docs/ControlPlaneApiSkeleton.md`: production API surface for central SAML cert orchestration.
- `docs/ControlPlanePolicyMatrix.md`: least-privilege RBAC and scope matrix.
- `docs/AzureAutomationRunbook.md`: Azure timer automation flow and operational runbook.
