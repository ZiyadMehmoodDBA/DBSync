# Phase 2E — Security & Compliance Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend MSOSync from custom JWT + BCrypt baseline to enterprise-grade security with secrets abstraction, OIDC/OAuth2 SSO, TOTP MFA, API key authentication, service accounts, and tamper-evident audit logging.

**Architecture:** Six sequential sub-phases on a dedicated track running in parallel with Phase 2F. 2E.1 (ISecretsService) must land first; subsequent sub-phases build on it. Migrations M041–M044 land in order as sub-phases complete. No breaking changes to existing local auth — all new auth methods layer on top.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / Microsoft.AspNetCore.Authentication.OpenIdConnect / Otp.NET / Azure.Security.KeyVault.Secrets / Azure.Identity / React 19 / TypeScript / TanStack Query v5 / shadcn/ui

## Global Constraints

- C# 13 / .NET 9, no `dynamic`, `sealed internal` for non-public implementations
- `IOptions<T>` for all config, `ValidateOnStart()` for required sections
- EF Core 9 migrations in `src/MSOSync.Persistence/Migrations/`, `ModelSnapshot` updated per migration
- Migrations: M041 (OIDC + SyncUser extensions), M042 (TOTP MFA), M043 (API keys + service accounts), M044 (audit hash chain)
- `ISecretsService` registered before any consumer in DI ordering in `Program.cs`
- TOTP: RFC 6238, HMAC-SHA1, 30-second window, 6 digits, ±1 step tolerance
- API key format: user `msk_<8alphanum>_<32urlsafebase64>`, service account `msa_<8alphanum>_<32urlsafebase64>`
- All secret values stored hashed (SHA-256 hex) in DB; raw value returned once at creation only
- React 19 / TypeScript / TanStack Query v5 — no `onSuccess`/`onError` on `useQuery`
- All new admin endpoints require `AdminOnly` policy
- `git add` by file name only — never `git add -A` or `git add .`
- Never commit `.env`, `*.pem`, `*.key`, `credentials.json`

---

## Sub-Phases

| Sub-phase | Plan file | Tasks | Migration |
|---|---|---|---|
| 2E.1 Secrets Abstraction | [2026-07-28-phase-2E-1-secrets-abstraction.md](2026-07-28-phase-2E-1-secrets-abstraction.md) | 4 | none |
| 2E.2 Azure Key Vault | [2026-07-28-phase-2E-2-azure-keyvault.md](2026-07-28-phase-2E-2-azure-keyvault.md) | 3 | none |
| 2E.3 OIDC/OAuth2 | [2026-07-28-phase-2E-3-oidc-oauth2.md](2026-07-28-phase-2E-3-oidc-oauth2.md) | 4 | M041 |
| 2E.4 TOTP MFA | [2026-07-28-phase-2E-4-totp-mfa.md](2026-07-28-phase-2E-4-totp-mfa.md) | 4 | M042 |
| 2E.5 API Keys + Service Accounts | [2026-07-28-phase-2E-5-api-keys-service-accounts.md](2026-07-28-phase-2E-5-api-keys-service-accounts.md) | 4 | M043 |
| 2E.6 Audit Hardening + Security Dashboard | [2026-07-28-phase-2E-6-audit-security-dashboard.md](2026-07-28-phase-2E-6-audit-security-dashboard.md) | 4 | M044 |

## Execution Order

```
2E.1 → 2E.2 → 2E.3 → 2E.4 → 2E.5 → 2E.6
(parallel with Phase 2F; 2E.1 starts simultaneously with 2F.1)
```

## Spec Reference

`docs/superpowers/specs/2026-07-28-phase-2E-security-compliance.md`
