# Control Plane API Skeleton for Managed SAML Apps

This document defines a production-oriented API surface for managing SAML certificate rotation from a central control plane (for example, the app in `Saml Cert Rotation`).

## Goals

- Allow central orchestration of discovery, staging, activation, and rollback.
- Keep legacy cert CRUD endpoints for compatibility during migration.
- Support idempotent and auditable automation.

## Security Model

- Authentication: Microsoft Entra OAuth2 Client Credentials.
- Authorization scopes:
  - `cert.read`
  - `cert.rotate`
  - `cert.activate`
  - `cert.rollback`
- Mutation endpoints require:
  - `Idempotency-Key` header
  - `If-Match` header (connection version/etag)

## Resource Model

- `IdpConnection`
  - `connectionId`
  - `metadataUrl`
  - `expectedAudiences[]`
  - `version`
- `Certificate`
  - `certId`
  - `thumbprint`
  - `state`: `staged | active | retiring | retired`
  - `notBeforeUtc`
  - `notAfterUtc`
  - `source`: `metadata | manual`

## Endpoints

### 1) Read state

- `GET /v1/idp-connections/{connectionId}`
- Scope: `cert.read`
- Returns current cert set, metadata URL, expected audiences, and `etag`.

### 2) Discover / stage

- `POST /v1/idp-connections/{connectionId}/rotation/discover`
- Scope: `cert.rotate`
- Headers: `Idempotency-Key`, `If-Match`
- Body:

```json
{
  "force": false,
  "dryRun": true
}
```

- Returns:

```json
{
  "metadataCheckedUtc": "2026-03-12T18:00:00Z",
  "metadataUrl": "https://login.microsoftonline.com/.../federationmetadata.xml?appid=...",
  "added": [],
  "unchanged": [],
  "expiringSoon": [],
  "warnings": [],
  "hasChanges": false
}
```

### 3) Activate

- `POST /v1/idp-connections/{connectionId}/rotation/activate`
- Scope: `cert.activate`
- Headers: `Idempotency-Key`, `If-Match`
- Body:

```json
{
  "targetCertIds": ["2f7f5a90-e6a8-49d6-b4db-c14eec2e2abc"],
  "overlapUntilUtc": "2026-04-12T00:00:00Z",
  "reason": "Scheduled monthly rotation"
}
```

- Returns `202 Accepted` with operation metadata.

### 4) Rollback

- `POST /v1/idp-connections/{connectionId}/rotation/rollback`
- Scope: `cert.rollback`
- Headers: `Idempotency-Key`, `If-Match`
- Body:

```json
{
  "toVersion": 41,
  "reason": "Validation failures after activation"
}
```

### 5) Logs / audit

- `GET /v1/idp-connections/{connectionId}/logs`
- Scope: `cert.read`
- Returns discover/activate/rollback/scheduler events.

## Mapping to Current Sample Endpoints

Current sample endpoints can remain available while introducing v1 control-plane routes:

- `GET /api/samlcerts` -> maps to state read.
- `GET /api/samlcerts/{id}` -> maps to certificate detail.
- `PATCH /api/samlcerts/{id}/primary` -> temporary activation shortcut.
- `DELETE /api/samlcerts/{id}` -> temporary retire/delete shortcut.

Use these old endpoints only for manual fallback after introducing `/v1/idp-connections/*`.

## Recommended Transition Plan

1. Add `/v1/idp-connections/*` endpoints as orchestration API.
2. Move scheduler/control-plane automation to `discover` + `activate` flow.
3. Restrict legacy cert CRUD endpoints to admin-only fallback.
4. Deprecate legacy endpoints after automation migration is complete.
