# Publishing to the Marketplace

Share your plugin with the MSOSync community.

## Prerequisites

1. **MSOSync Account** — Create one at https://plugins.msosync.io
2. **Signed `.msopkg`** — Package and sign (see [Packaging](packaging.md))
3. **CLI Installed** — `msosync` CLI tool version 1.0+

## First-Time Authentication

```bash
msosync login
```

Enter your MSOSync account email and password. Token is stored in:
- **Windows:** `%APPDATA%\MSOSync\auth.json`
- **macOS/Linux:** `~/.msosync/auth.json`

[CLI: pending 2C.4 finalization]

## Publishing Your Plugin

```bash
msosync plugin publish ./dist/acme-my-plugin-1.0.0.msopkg
```

**Output:**
```
Published: acme-my-plugin v1.0.0
URL: https://plugins.msosync.io/acme/my-plugin
```

The marketplace validates:
- ✓ Signature is valid
- ✓ `plugin.json` is valid
- ✓ SDK version range is compatible with current host
- ✓ No blacklisted permissions declared without approval

[CLI: pending 2C.4 finalization]

## Versioning Rules

**Semantic Versioning required:** `MAJOR.MINOR.PATCH`

- **Patch bump (1.0.0 → 1.0.1):** Bug fixes
- **Minor bump (1.0.0 → 1.1.0):** New features, backward compatible
- **Major bump (1.0.0 → 2.0.0):** Breaking changes

**Once published, a version cannot be overwritten.** Incremental versioning is enforced.

## Pre-Release Versions

```bash
msosync plugin publish ./dist/acme-my-plugin-1.1.0-beta.1.msopkg \
  --pre
```

Pre-release versions are visible in the marketplace but not selected by default when users install "latest."

[CLI: pending 2C.4 finalization]

## Marketplace Review

**Normal plugins:** Published immediately

**Plugins declaring `Operations` permission:** Subject to manual review
- Estimated review window: 2–5 business days
- Marketplace team verifies plugin behavior matches declared intent
- Approved plugins become public

**Other blacklisted permissions:** Contact support at support@msosync.io for exceptions.

## Updating an Existing Plugin

1. Bump `version` in `plugin.json`
2. Rebuild: `dotnet build`
3. Repack: `msosync plugin pack`
4. Publish: `msosync plugin publish ./dist/acme-my-plugin-1.1.0.msopkg`

Marketplace keeps all published versions. Users choose upgrade timing.

## Deprecating a Version

If you discover a critical bug in v1.0.0 after publishing v1.1.0:

```bash
msosync plugin deprecate acme-my-plugin@1.0.0
```

Deprecated versions remain installable but show a warning: "This version is deprecated. Please upgrade to v1.1.0+."

[CLI: pending 2C.4 finalization]

## Plugin Metadata on the Marketplace

The marketplace displays:
- **Name** — from `plugin.json`
- **Description** — from `plugin.json`
- **Version** — from `plugin.json`
- **Author** — from `plugin.json`
- **Capabilities** — from `plugin.json`
- **Permissions** — from `plugin.json`
- **README** — if `README.md` is at package root

### Including a README in Your Package

Create `README.md` in your plugin directory. The `msosync plugin pack` command automatically includes it.

**Contents:** Installation instructions, configuration reference, examples.

## User Installation

Users install published plugins via the marketplace UI or CLI:

```bash
# Via CLI
msosync plugin install acme-my-plugin

# Via marketplace web UI
# (download and place in {host}/plugins/{plugin-id}/)
```

## Troubleshooting

### "Signature validation failed"

Ensure you signed the `.msopkg` before publishing:
```bash
msosync plugin sign ./dist/acme-my-plugin-1.0.0.msopkg --key ~/.msosync/keys/acme-key.pem
```

### "SDK version not compatible"

Your `plugin.json` declares `sdkVersion: "1.0"` but the host is running SDK 0.9. Update host or adjust SDK version range.

### "Permission requires approval"

Some permissions are blacklisted for security reasons. Contact support@msosync.io.

## Next Steps

- See [Permissions](permissions.md) for permission best practices
- See [Configuration](configuration.md) for configuration documentation
