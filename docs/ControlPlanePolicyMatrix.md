# Control Plane RBAC Policy Matrix

This matrix defines least-privilege access for humans and workload identities.

## Roles

- `ControlPlane.Reader`
- `ControlPlane.Operator`
- `ControlPlane.Activator`
- `ControlPlane.BreakGlass`
- `ManagedApp.Admin` (local app admin for emergency/manual operations)

## Permissions Matrix

| Capability | Reader | Operator | Activator | BreakGlass | ManagedApp.Admin |
|---|---:|---:|---:|---:|---:|
| View connection state | Yes | Yes | Yes | Yes | Yes |
| View logs/audit | Yes | Yes | Yes | Yes | Yes |
| Discover/stage certs | No | Yes | Yes | Yes | Optional |
| Activate certs | No | No | Yes | Yes | Optional |
| Rollback cert set | No | No | No | Yes | Optional |
| Change schedules/policies | No | Yes | No | Yes | Local only |
| Manual direct cert CRUD | No | No | No | Yes | Yes |

## OAuth2 Scope Mapping

| Scope | Typical Assignee |
|---|---|
| `cert.read` | Reader, Operator, Activator |
| `cert.rotate` | Operator workload identity |
| `cert.activate` | Activator workload identity |
| `cert.rollback` | BreakGlass human-operated identity |

## Guardrails

1. Require `Idempotency-Key` for all mutating endpoints.
2. Require `If-Match` to prevent stale writes.
3. Log actor, correlation ID, request hash, and outcome for every mutation.
4. Enforce overlap window for activation and block hard cutover.
5. Keep rollback rights isolated to break-glass role only.

## Operational Recommendation

- Use separate service principals for:
  - discovery/staging
  - activation
- Keep break-glass as a human-run path with approval.
- Enable Conditional Access and app assignment required.
