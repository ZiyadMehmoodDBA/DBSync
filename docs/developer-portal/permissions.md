# Plugin Permissions Model

Permissions declare what host resources a plugin may need. In SDK 1.0, they are **informational**. In future phases, enforcement will arrive.

## What Are Permissions?

Declared intent in `plugin.json`:

```json
{
  "permissions": ["Collectors", "Transport"]
}
```

These strings correspond to `PluginPermission` enum values:

| Value | Meaning |
|-------|---------|
| `None` | No special access required (default) |
| `Collectors` | Plugin reads data from external sources |
| `Transport` | Plugin makes outbound network calls |
| `Operations` | Plugin performs mutation/operational actions |

## Capabilities vs Permissions

**Capability:** What the plugin does
- `Collector`, `Transport`, `Operation`, `Router`, `Health`

**Permission:** What host resources it needs
- `Collectors`, `Transport`, `Operations`

**Relationship:** A plugin declaring capability `Collector` typically also declares permission `Collectors`.

### Example Declarations

**Data Collector (reads from DB):**
```json
{
  "capabilities": ["Collector"],
  "permissions": ["Collectors"]
}
```

**Webhook Plugin (posts to external URL):**
```json
{
  "capabilities": ["Transport"],
  "permissions": ["Transport"]
}
```

**Multi-permission Plugin (polls + posts):**
```json
{
  "capabilities": ["Collector", "Transport"],
  "permissions": ["Collectors", "Transport"]
}
```

**Passive Config Reader (no special access):**
```json
{
  "capabilities": [],
  "permissions": []
}
```

## Declaring Permissions in plugin.json

```json
{
  "manifestVersion": 1,
  "id": "acme.my-plugin",
  "name": "My Plugin",
  ...
  "permissions": ["Collectors", "Transport"],
  "capabilities": ["Collector", "Transport"]
}
```

Unknown permission strings are logged as warnings and ignored.

## Enforcement (Future)

**Current state (SDK 1.0):**
- Permissions are read and logged
- No runtime enforcement
- Plugin can function regardless

**Future (1.1+):**
- Admin must explicitly grant declared permissions in host configuration
- Plugin fails to load if permissions not granted
- Audit trail of permission grants

**Why declare them now?**
- Document your plugin's access model
- Prepare for future enforcement
- Enable permission-based filtering in the marketplace

## Combined Permissions

A plugin may declare multiple permissions:

```json
{
  "permissions": ["Collectors", "Transport", "Operations"]
}
```

This is valid and common for complex plugins.

## Best Practices

- **Declare only the permissions you actually use** — more permissions = higher user friction in future phases
- **Match capabilities to permissions** — if you declare `Collector` capability, also declare `Collectors` permission
- **Document why** — add README section explaining what external access the plugin requires
- **Test with future enforcement in mind** — assume permissions will be denied and test graceful fallback

## Permission Reference

| Permission | Integer | Meaning | Example Use Case |
|-----------|---------|---------|---|
| `None` | 0 | No special access required | Configuration validator plugin |
| `Collectors` | 1 | Read from external data sources | SQL database poller, file reader |
| `Transport` | 2 | Make outbound network calls | Webhook sender, syslog forwarder |
| `Operations` | 4 | Perform mutations | Sync trigger, event publisher |
