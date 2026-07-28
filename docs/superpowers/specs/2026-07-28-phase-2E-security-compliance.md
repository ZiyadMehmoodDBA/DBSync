# Phase 2E — Security & Compliance Design

**Status:** Approved — 2026-07-28
**Parallel track:** Runs alongside Phase 2F. 2E.1 dispatched simultaneously with 2F.1.
**Base commit:** e4cc210

---

## Overview

Phase 2E extends MSOSync's security posture from a custom JWT + BCrypt baseline into an enterprise-grade security platform. It adds external identity provider integration (OIDC only), a provider-agnostic secrets abstraction, TOTP-based MFA for local accounts, API key and service account authentication, and a tamper-evident audit system with a security dashboard.

**Out of scope for 2E:** Azure AD native (Conditional Access, Graph API), LDAP, SAML, certificate authentication (mTLS), HashiCorp Vault, AWS Secrets Manager, key rotation jobs (rotation delegated to the secret provider).

---

## Architecture

MSOSync's existing security layer (`MSOSync.Security`) already contains: JWT generation/validation, BCrypt password hashing, node token middleware, rate limiting, security headers middleware, and RBAC via `IPermissionService`. Phase 2E builds on top of this without replacing it.

A new project `MSOSync.Secrets` provides the secrets abstraction. OIDC wiring lands in `MSOSync.Security`. MFA and API key auth land in `MSOSync.Security` and `MSOSync.Api`. Audit hardening lands in `MSOSync.Metadata.Audit`. The security dashboard is a new React page in `MSOSync.Frontend`.

---

## Global Constraints

- C# 13 / .NET 9, no `dynamic`, `sealed internal` for non-public implementations
- `IOptions<T>` pattern for all configuration; validate on start
- EF Core 9 migrations in `MSOSync.Persistence/Migrations/`, `ModelSnapshot` updated per migration
- Migrations numbered sequentially: M041 (OIDC), M042 (TOTP), M043 (API keys + service accounts), M044 (audit hash chain)
- `ISecretsService` must be registered before any service that previously read secrets from `IConfiguration` or env vars directly
- TOTP: RFC 6238 (HMAC-SHA1, 30-second window, 6 digits), 1 step tolerance (±30 seconds)
- API key format: user keys `msk_<8-char-prefix>_<32-char-random-urlsafe>`, service account keys `msa_<8-char-prefix>_<32-char-random-urlsafe>`
- All secret values stored hashed (SHA-256) in DB; raw value shown once at creation only
- React 19 / TypeScript / TanStack Query v5 — no `onSuccess`/`onError` on `useQuery`
- All new admin endpoints require `AdminOnly` policy
- Parallel execution: 2E.1 runs first; 2E.2–2E.6 run sequentially on the 2E track

---

## Sub-Phases

### 2E.1 — Secrets Abstraction

**New project:** `MSOSync.Secrets` (class library)

**Interface:**
```csharp
public interface ISecretsService
{
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);
    Task<byte[]?> GetSecretBytesAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
```

**Implementations:**
- `EnvironmentSecretsService` — reads `Environment.GetEnvironmentVariable(key.Replace(":", "__"))`. In `Development` environment, falls back to `IConfiguration[key]` when env var absent.
- `CompositeSecretsService(IEnumerable<ISecretsService> providers)` — iterates providers in order, returns first non-null result. Registered providers in order: `AzureKeyVaultSecretsService` (if configured), `EnvironmentSecretsService`.

**Configuration:**
```json
{
  "Secrets": {
    "Provider": "Environment"
  }
}
```
`Provider` values: `"Environment"` (default), `"AzureKeyVault"`. When `"AzureKeyVault"`, `AzureKeyVaultSecretsService` is prepended to the composite chain.

**Registration:**
```csharp
public static IServiceCollection AddSecretsService(
    this IServiceCollection services, IConfiguration config)
```
Registered as `ISecretsService` singleton backed by `CompositeSecretsService`.

**Migration of existing secrets:**
- `MSOSYNC_JWT_SECRET` → continue reading from env var via `EnvironmentSecretsService` using key `"Jwt:SigningKey"`. `SecurityServiceExtensions` updated to call `ISecretsService.GetSecretAsync("Jwt:SigningKey")` at startup (before `IOptions<JwtOptions>` bind).
- `Pagination:CursorHmacKey` → `ISecretsService.GetSecretAsync("Pagination:CursorHmacKey")`
- `MSOSYNC_NODE_TOKEN` → `ISecretsService.GetSecretAsync("Node:BootstrapToken")`

**Tests:** Unit tests for `EnvironmentSecretsService` and `CompositeSecretsService` using environment variable overrides and mock providers.

---

### 2E.2 — Azure Key Vault

**Packages:**
- `Azure.Security.KeyVault.Secrets`
- `Azure.Identity`

**Configuration:**
```json
{
  "Secrets": {
    "Provider": "AzureKeyVault",
    "AzureKeyVault": {
      "VaultUri": "https://your-vault.vault.azure.net/",
      "CacheTtlSeconds": 300
    }
  }
}
```

**Implementation:**
```csharp
internal sealed class AzureKeyVaultSecretsService : ISecretsService
```
- Uses `SecretClient` with `DefaultAzureCredential` (Managed Identity in prod, CLI/env in dev)
- Key name mapping: `.` and `:` → `-` (Key Vault doesn't allow colon/dot in secret names)
- In-memory cache via `IMemoryCache` with `CacheTtlSeconds` sliding expiry
- Returns `null` (not throws) when secret not found (`RequestFailedException` with status 404)
- Throws on non-404 errors (network failure, auth failure)

**Health contributor:**
```csharp
internal sealed class KeyVaultHealthContributor : ISystemHealthContributor
```
Checks `SecretClient.GetPropertiesOfSecretsAsync()` can enumerate at least one page. Reports `Degraded` (not `Unhealthy`) to allow app to start without vault (env vars fall through).

**Registration:** `AddAzureKeyVaultSecrets(config)` extension method. Only registered when `Secrets:Provider == "AzureKeyVault"`.

**Tests:** Unit tests with `SecretClient` mocked via `Moq`. Integration test requires `AZURE_KEY_VAULT_URI` env var (skipped in CI).

---

### 2E.3 — OIDC/OAuth2

**Package:** `Microsoft.AspNetCore.Authentication.OpenIdConnect`

**Entity:**
```csharp
// Migration M041
public sealed class OidcConfiguration
{
    public int Id { get; set; }
    public string ProviderName { get; set; }      // e.g. "google", "okta", "azure"
    public string Authority { get; set; }           // IdP discovery URL base
    public string ClientId { get; set; }
    public string ClientSecretKey { get; set; }    // ISecretsService key, not the secret itself
    public string Scopes { get; set; }             // space-separated, default "openid profile email"
    public string? NameClaimType { get; set; }     // default "name"
    public string? RoleClaimType { get; set; }     // optional, maps to MSOSync role
    public bool AutoProvisionUsers { get; set; }   // create SyncUser on first login
    public bool Enabled { get; set; }
}
```

**Schema additions to `SyncUser` (M041):**
- `external_id nvarchar(500) NULL` — OIDC `sub` claim
- `auth_provider nvarchar(100) NULL` — e.g. `"local"`, `"oidc:google"`
- `email nvarchar(500) NULL`

**Login flow:**
1. `GET /api/v1/auth/oidc/{providerName}/login?returnUrl=...` → `Challenge(providerName)` → redirect to IdP
2. IdP redirects to `GET /api/v1/auth/oidc/{providerName}/callback`
3. Handler: lookup/create `SyncUser` by `external_id`; if `AutoProvisionUsers = false` and user not found → 403
4. Issue MSOSync JWT pair (same `JwtService` as local login)
5. Redirect to `returnUrl` with tokens in fragment or set cookie

**`OidcController`** (admin-only):
- `GET /api/v1/admin/oidc` — list configured providers
- `POST /api/v1/admin/oidc` — create provider config
- `PUT /api/v1/admin/oidc/{id}` — update (ClientSecretKey stored via `ISecretsService` key reference)
- `DELETE /api/v1/admin/oidc/{id}` — delete
- `POST /api/v1/admin/oidc/{id}/test` — validates discovery document reachable, returns issuer metadata

**Registration:** Dynamic OIDC schemes registered at startup from active `OidcConfiguration` rows. `IOptionsMonitor<AuthenticationOptions>` used for runtime-configurable schemes (or restart-required with clear documentation).

**Tenant resolution:** OIDC callback sets tenant from claim (configurable claim name) or defaults to primary tenant.

**Tests:** Integration tests with `TestServer` + mock OIDC server (`IdentityModel.OidcClient.Testing` or manual `HttpMessageHandler` mock returning valid OIDC discovery + token responses).

---

### 2E.4 — TOTP MFA

**Package:** `Otp.NET` (NuGet: `Otp.Net`)

**Schema additions to `SyncUser` (M042):**
- `mfa_enabled bit NOT NULL DEFAULT 0`
- `mfa_secret_key nvarchar(500) NULL` — base32-encoded TOTP seed, encrypted via ASP.NET DataProtection `CreateProtector("MFA")`
- `mfa_enrolled_at datetimeoffset NULL`

**New entity `SyncUserBackupCode` (M042):**
```csharp
public sealed class SyncUserBackupCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string CodeHash { get; set; }   // SHA-256 of plaintext backup code
    public DateTimeOffset? UsedAt { get; set; }
    public SyncUser User { get; set; }
}
```
8 backup codes generated at enrollment, each 8 alphanumeric characters (`XXXX-XXXX` display format).

**`IMfaService`:**
```csharp
public interface IMfaService
{
    string GenerateSecret();
    string GetQrCodeUri(string secret, string username, string issuer);
    bool VerifyCode(string secret, string code);
    Task<string[]> GenerateBackupCodesAsync(int userId, CancellationToken ct);
    Task<bool> UseBackupCodeAsync(int userId, string code, CancellationToken ct);
}
```

**Endpoints:**
- `POST /api/v1/auth/mfa/enroll` — generates TOTP secret, returns `{ qrCodeUri, secretKey }`. Does NOT enable MFA yet.
- `POST /api/v1/auth/mfa/verify` — verifies TOTP code against pending secret; if valid, enables MFA and returns backup codes array. Idempotent.
- `POST /api/v1/auth/mfa/disable` — requires current TOTP code or backup code; clears `mfa_enabled`, `mfa_secret_key`, deletes backup codes.
- `POST /api/v1/auth/mfa/challenge` — called during login with `mfa_token` + `code`; returns full access + refresh tokens.
- `POST /api/v1/auth/mfa/backup-codes/regenerate` — requires TOTP code; deletes existing codes, generates 8 new ones.

**Login flow change in `AuthenticationService.LoginAsync`:**
- After password verification succeeds: if `user.MfaEnabled` → return `MfaRequired = true` + short-lived `mfa_token` JWT (5-minute expiry, claim `purpose = mfa_challenge`). No access/refresh tokens.
- Client POSTs TOTP code to `/api/v1/auth/mfa/challenge` with `mfa_token` in header.
- `MfaChallengeEndpoint` validates `mfa_token` + TOTP code → issues full JWT pair.

**Tests:** Unit tests for `MfaService` (known TOTP seed, known code, known timestamp). Integration tests for full enroll → verify → login with MFA flow.

---

### 2E.5 — API Keys + Service Accounts

**Entities (M043):**
```csharp
public sealed class SyncUserApiKey
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; }
    public string KeyPrefix { get; set; }       // 8 chars, stored plaintext for lookup
    public string KeyHash { get; set; }          // SHA-256 of full key, hex-encoded
    public string Scopes { get; set; }           // JSON array of permission keys
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public SyncUser User { get; set; }
}

public sealed class SyncServiceAccount
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string KeyPrefix { get; set; }
    public string KeyHash { get; set; }
    public string PermissionIds { get; set; }    // JSON array
    public int CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
```

**Key format:**
- User API key: `msk_<8-char-alphanum>_<32-char-urlsafe-base64>`
- Service account key: `msa_<8-char-alphanum>_<32-char-urlsafe-base64>`
- Full key never stored; only `KeyPrefix` (first 8 chars after `msk_`/`msa_`) + SHA-256 hash.
- Lookup by prefix (indexed), verify by `SHA256(presented_key) == stored_hash`.

**`IApiKeyService`:**
```csharp
public interface IApiKeyService
{
    Task<ApiKeyCreateResult> CreateUserKeyAsync(int userId, string name, string[] scopes, DateTimeOffset? expiresAt, CancellationToken ct);
    Task<ApiKeyCreateResult> CreateServiceAccountKeyAsync(string name, string[] permissionIds, int createdBy, DateTimeOffset? expiresAt, CancellationToken ct);
    Task<ApiKeyPrincipal?> ValidateAsync(string rawKey, CancellationToken ct);
    Task RevokeAsync(int keyId, ApiKeyType type, CancellationToken ct);
}
```
`ApiKeyCreateResult` includes `RawKey` (only returned here), `KeyPrefix`, `Id`.
`ApiKeyPrincipal` includes `UserId?`, `ServiceAccountId?`, `Scopes`, `PermissionIds`.

**`ApiKeyAuthenticationHandler`:**
- Reads `Authorization: ApiKey <key>` header
- Extracts prefix from key, queries by prefix, verifies SHA-256 hash
- Updates `LastUsedAt` (background fire-and-forget, no await on hot path)
- Sets `ClaimsPrincipal` with scopes/permissions as claims
- Falls through (not 401) if header absent — allows JWT bearer to handle

**Endpoints:**
- `GET/POST/DELETE /api/v1/user/api-keys` — user manages own keys
- `GET/POST/DELETE /api/v1/admin/service-accounts` — admin manages service accounts

**Tests:** Unit tests for `ApiKeyService` (hash verification, expiry check, revocation). Integration test for `Authorization: ApiKey <key>` header on protected endpoint.

---

### 2E.6 — Audit Hardening + Security Dashboard

**Tamper-evident audit chain (M044):**

Schema addition to `SyncAudit`:
- `prev_hash nvarchar(64) NULL` — SHA-256 hex of `(prev_entry.prev_hash + prev_entry.id + prev_entry.action_name + prev_entry.username + prev_entry.create_time_utc)`
- Null only on the first ever audit record
- `AuditService.WriteAsync` computes hash of last record + new fields before insert

**Additional audit events emitted in 2E:**
- `OidcLogin` — `{provider, sub, email, provisioned: bool}`
- `MfaEnroll` — `{userId}`
- `MfaChallenge` — `{userId, success: bool}`
- `ApiKeyCreated` — `{keyId, name, scopes}`
- `ApiKeyRevoked` — `{keyId}`
- `ServiceAccountCreated` — `{accountId, name}`
- `OidcConfigChanged` — `{providerId, action: created|updated|deleted}`

**Data masking:**
- `ConnectionStringMaskingDestructuringPolicy : IDestructuringPolicy` added to Serilog — replaces values of properties named `ConnectionString`, `Password`, `Token`, `Secret`, `Key` with `"***"` in structured logs
- Response DTOs: any DTO exposing `DatabasePassword`, `ConnectionString`, or `ApiKey` fields masks them as `"***"` in all non-admin responses. `SyncNodeDto.ConnectionString` already masked in prior epics — verify and extend.

**`SecurityAuditController`:**
- `GET /api/v1/admin/security/audit?page=&pageSize=&eventType=&from=&to=` — paginated audit log filtered by security event types
- `GET /api/v1/admin/security/audit/verify` — verifies hash chain integrity, returns `{ valid: bool, firstInvalidId: int? }`

**Frontend — `SecurityDashboardPage`:**
- Route: `/administration/security` (AdminOnly)
- Sections:
  - **Auth Events Timeline** — last 7 days, login successes/failures by hour (bar chart)
  - **MFA Adoption** — `% of local users with MFA enabled` gauge
  - **API Keys** — count of active user keys + service accounts, table of recently used
  - **OIDC Providers** — list of configured providers with status (reachable/error)
  - **Recent Failures** — last 20 failed login attempts (username, IP, timestamp)

---

## Execution Order

```
2E.1 (Secrets Abstraction)
    ↓
2E.2 (Azure Key Vault)
    ↓
2E.3 (OIDC/OAuth2)        ← M041
    ↓
2E.4 (TOTP MFA)           ← M042
    ↓
2E.5 (API Keys + SAs)     ← M043
    ↓
2E.6 (Audit + Dashboard)  ← M044
```

Sequential within 2E track; parallel with 2F track. 2F.1 starts simultaneously with 2E.1.
