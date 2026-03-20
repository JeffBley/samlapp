# Azure Automation Runbook for Central SAML Rotation

This runbook aligns the managed app API with the serverless architecture in `Saml Cert Rotation`.

## Objective

Use the central control-plane app to orchestrate certificate rotation on managed SAML apps via API.

## Components

- Azure Functions Timer (central scheduler)
- Managed identity / service principal for API calls
- App Insights + Log Analytics for telemetry
- Optional Service Bus for retry queueing

## Daily Flow

1. Timer trigger starts at configured UTC schedule.
2. For each managed SAML app connection:
   - `POST /v1/idp-connections/{id}/rotation/discover` with `dryRun=false`
3. Evaluate response:
   - if `hasChanges=false`, write scheduler audit: no change.
   - if `hasChanges=true`, apply policy gate checks.
4. If policy allows activation:
   - `POST /v1/idp-connections/{id}/rotation/activate`
5. Validate health signals for fixed window.
6. On failure threshold breach:
   - `POST /v1/idp-connections/{id}/rotation/rollback`
7. Emit consolidated report and alerts.

## Retry and Idempotency

- Generate deterministic `Idempotency-Key` per app + operation + schedule window.
- On transient failures, retry with same key.
- Treat `409/412` as concurrency control events; re-read state and re-plan.

## Observability

- Metrics:
  - `discover_runs_total`
  - `discover_changes_total`
  - `activation_success_total`
  - `activation_failure_total`
  - `rollback_total`
- Logs:
  - Include `connectionId`, `operationId`, `correlationId`, `actor`, `result`.
- Alerts:
  - Activation failures > threshold
  - Expiring cert with no staged successor
  - Consecutive no-contact failures for metadata URL

## Security Checklist

1. Use Entra OAuth2 client credentials for machine API access.
2. Keep secrets in Key Vault; avoid static API keys for production automation.
3. Enforce HTTPS and certificate pinning controls where possible.
4. Restrict metadata URLs to approved allowlist.
5. Apply least-privilege scopes and separate identities by operation.

## Pilot to Production Steps

1. Pilot with report-only mode for 2-4 weeks.
2. Enable activation only for low-risk app cohort.
3. Add rollback automation after health checks are proven.
4. Expand by app tier with change windows and approval gates.
