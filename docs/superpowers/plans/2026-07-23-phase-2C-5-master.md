# Phase 2C.5 — SDK Samples + Developer Portal (Master Plan)

**Date:** 2026-07-23  
**Phase:** 2C — SDK & Ecosystem  
**Status:** Ready for Execution  
**Executor:** Agent  
**Estimated Duration:** 2–3 days (autonomous execution)

---

## Overview

Phase 2C.5 converts MSOSync from a platform with an SDK into a developer-friendly ecosystem: four official code samples demonstrating the full SDK surface, two `dotnet new` project templates for plugin scaffolding, and a comprehensive Markdown developer portal.

**Completion criteria (all must be true):**

1. All four samples compile with `dotnet build --warnaserror` against `MSOSync.Sdk`
2. No sample references `MSOSync.Api`, `MSOSync.Metadata`, `MSOSync.Plugin`, or `MSOSync.Persistence`
3. `dotnet new install MSOSync.Templates` completes without error
4. Both templates scaffold and compile with zero errors and zero warnings
5. All eight portal Markdown files exist under `docs/developer-portal/` with no broken links
6. `samples/build-check.ps1` builds all four samples and exits non-zero on any failure

---

## Execution Strategy

This plan breaks 2C.5 into **five independent tasks**, each delivering a complete, testable artifact:

| Task | Deliverable | Time | Dependencies |
|------|-------------|------|---|
| **1** | HelloWorldPlugin + DataCollectorPlugin | 6h | None |
| **2** | WebhookPlugin + ConfigDrivenPlugin | 6h | Task 1 structure pattern |
| **3** | MSOSync.Templates (2 templates) | 4h | Samples 1–4 complete |
| **4** | Developer Portal (8 Markdown pages) | 5h | Samples 1–4, Templates |
| **5** | samples/build-check.ps1 + validation | 2h | Samples 1–4 + Portal |

**Total estimated effort:** ~23 hours of autonomous execution.

**Parallel opportunities:** Tasks 1–2 can run in parallel (independent samples). Task 3 waits for Tasks 1–2. Tasks 4–5 wait for Task 3.

**Execution mode:** Proceed autonomously to completion without confirmation between tasks. No pauses for approval.

---

## Task Sequencing

### Phase 1: Samples (Tasks 1–2)

- **Task 1:** Build HelloWorldPlugin and DataCollectorPlugin with full implementation, manifests, config, and READMEs.
- **Task 2:** Build WebhookPlugin and ConfigDrivenPlugin with full implementation, manifests, config, and READMEs.

**Validation:** Each sample compiles with `dotnet build --warnaserror`.

### Phase 2: Templates (Task 3)

- **Task 3:** Create MSOSync.Templates project with two templates (`msosync-plugin` basic, `msosync-plugin-advanced` with config/services).

**Validation:** Both templates scaffold cleanly and compile.

### Phase 3: Portal + Verification (Tasks 4–5)

- **Task 4:** Write all eight Markdown pages with complete, non-outline content and cross-links.
- **Task 5:** Create build validation script, run all checks, commit everything.

**Validation:** Links resolve, all builds pass, CI script exits 0.

---

## Key Constraints

### SDK Isolation
All samples reference **only** `MSOSync.Sdk`. No API, Persistence, Common, Metadata, or Plugin dependencies. Enforced by `.csproj` structure.

### Warning-Free Builds
All samples and templates must build with `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — inherited from `Directory.Build.props`.

### Framework & Language Versions
All samples and templates target `net9.0`, C# 13, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.

### Template SDK Reference Pattern
Templates use conditional MSBuild logic to switch between ProjectReference (dev) and PackageReference (published):

```xml
<ItemGroup Condition="'$(MSOSyncSdkLocal)' == 'true'">
  <ProjectReference Include="..\..\src\MSOSync.Sdk\MSOSync.Sdk.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(MSOSyncSdkLocal)' != 'true'">
  <PackageReference Include="MSOSync.Sdk" Version="1.0.0" />
</ItemGroup>
```

CI build sets `/p:MSOSyncSdkLocal=true`.

### Portal Format
Markdown only. No HTML, no YAML front-matter, no shortcodes. Fenced code blocks with language tags. All links relative (`[link](file.md)`).

### Solution File Integrity
`MSOSync.sln` is modified **only** to add `src/MSOSync.Templates/MSOSync.Templates.csproj` under a `Templates` solution folder (marked as excluded from default build). Sample projects under `samples/` are **not** added to the solution.

---

## Sample Structure Recap

```
samples/
├── HelloWorldPlugin/
│   ├── HelloWorldPlugin.csproj
│   ├── HelloWorldPlugin.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
├── DataCollectorPlugin/
│   ├── DataCollectorPlugin.csproj
│   ├── DataCollectorPlugin.cs
│   ├── MetricSample.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
├── WebhookPlugin/
│   ├── WebhookPlugin.csproj
│   ├── WebhookPlugin.cs
│   ├── WebhookPayload.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
├── ConfigDrivenPlugin/
│   ├── ConfigDrivenPlugin.csproj
│   ├── ConfigDrivenPlugin.cs
│   ├── PluginSettings.cs
│   ├── plugin.json
│   ├── plugin.config.json
│   └── README.md
└── build-check.ps1
```

## Template Structure Recap

```
src/MSOSync.Templates/
├── MSOSync.Templates.csproj
├── README.md
└── content/
    ├── msosync-plugin/
    │   ├── .template.config/template.json
    │   ├── MyPlugin.cs
    │   ├── MyPlugin.csproj
    │   ├── plugin.json
    │   └── plugin.config.json
    └── msosync-plugin-advanced/
        ├── .template.config/template.json
        ├── MyPlugin.cs
        ├── MyPluginSettings.cs
        ├── MyPlugin.csproj
        ├── plugin.json
        └── plugin.config.json
```

## Portal Structure Recap

```
docs/developer-portal/
├── getting-started.md          (5-minute quick start)
├── plugin-lifecycle.md         (lifecycle phases & contract)
├── configuration.md            (IPluginConfiguration guide)
├── services.md                 (IPluginServices guide)
├── permissions.md              (permission model)
├── packaging.md                (how to create .msopkg)
├── publishing.md               (how to publish to marketplace)
└── api-reference.md            (all SDK interfaces documented)
```

---

## Implementation Tasks

### Task 1: HelloWorldPlugin + DataCollectorPlugin
→ [2026-07-23-phase-2C-5-task-1-hello-collector.md](2026-07-23-phase-2C-5-task-1-hello-collector.md)

### Task 2: WebhookPlugin + ConfigDrivenPlugin
→ [2026-07-23-phase-2C-5-task-2-webhook-config.md](2026-07-23-phase-2C-5-task-2-webhook-config.md)

### Task 3: MSOSync.Templates (Project Templates)
→ [2026-07-23-phase-2C-5-task-3-templates.md](2026-07-23-phase-2C-5-task-3-templates.md)

### Task 4: Developer Portal (8 Markdown Pages)
→ [2026-07-23-phase-2C-5-task-4-portal.md](2026-07-23-phase-2C-5-task-4-portal.md)

### Task 5: Build Validation + Final Commit
→ [2026-07-23-phase-2C-5-task-5-build-validation.md](2026-07-23-phase-2C-5-task-5-build-validation.md)

---

## Success Criteria Checklist

- [ ] Task 1 complete: HelloWorldPlugin and DataCollectorPlugin compile and include READMEs
- [ ] Task 2 complete: WebhookPlugin and ConfigDrivenPlugin compile and include READMEs
- [ ] Task 3 complete: MSOSync.Templates csproj created, both templates scaffold and compile
- [ ] Task 4 complete: All 8 portal Markdown files exist with content (no outlines)
- [ ] Task 5 complete: build-check.ps1 validates all samples, portal links check passes, everything committed

---

## Notes

- **Spec reference:** `docs/superpowers/specs/2026-07-23-phase-2C-5-sdk-samples-portal.md`
- **Autonomous execution:** Proceed to task 1 immediately after this plan is committed. Do not wait for confirmation between tasks.
- **Blockers:** None identified. All SDK interfaces are stable (14B finalized).
- **Review gate:** Manual validation required before merge (see spec section on Testing Approach).
