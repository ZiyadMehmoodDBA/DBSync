# Creating a Plugin Package (.msopkg)

Packages are required for marketplace submission. Optional for local deployment.

## What is a .msopkg?

A signed ZIP archive containing your plugin DLL, manifest, configuration, and optional private dependencies.

## .msopkg Internal Layout

```
acme-my-plugin-1.0.0.msopkg
├── plugin.json                    ← manifest (must be at root)
├── AcmeMyPlugin.dll               ← compiled plugin assembly
├── lib/                           ← (optional) private NuGet dependencies
│   ├── Microsoft.Data.SqlClient.dll
│   └── ...
├── plugin.config.json             ← (optional) default configuration
├── resources/                     ← (optional) static assets
│   └── ...
└── signature.sig                  ← (required for marketplace, optional for local)
```

## Local Deployment (No Packaging Required)

For testing locally, skip packaging. Deploy directly:

```bash
# After building your plugin
mkdir -p {host}/plugins/acme.my-plugin
cp -r ./bin/Release/net9.0/* {host}/plugins/acme.my-plugin/
```

The host discovers and loads the DLL directly.

## Packaging with the CLI

For marketplace submission or distribution:

```bash
dotnet build ./AcmeMyPlugin.csproj

msosync plugin pack ./AcmeMyPlugin.csproj --output ./dist
```

**Output:**
```
Created: dist/acme-my-plugin-1.0.0.msopkg
```

[CLI: pending 2C.4 finalization]

## Verifying the Package

```bash
msosync plugin verify ./dist/acme-my-plugin-1.0.0.msopkg
```

Checks:
- ✓ `plugin.json` is valid JSON
- ✓ All required fields present (`id`, `name`, `version`, `entryAssembly`, `entryType`, etc.)
- ✓ Entry assembly exists in package
- ✓ No path traversal (`../`) in file names

[CLI: pending 2C.4 finalization]

## Signing the Package

For marketplace submission, sign the package with your private Ed25519 key:

```bash
msosync plugin sign ./dist/acme-my-plugin-1.0.0.msopkg \
  --key ./acme-key.pem
```

**Output:**
```
Signed: signature.sig added to package
```

The signature proves you own the plugin and haven't tampered with contents.

[CLI: pending 2C.4 finalization]

## Private NuGet Dependencies

If your plugin uses NuGet packages not published to a public feed, include them in `lib/`:

```bash
# In your .csproj
<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
```

This ensures private DLLs are copied to build output, and then to `lib/` in the package.

**Warning:** Never commit private keys to version control. Store `.pem` files in `~/.msosync/keys/`.

## Package Size Limits

- **Single file:** 50 MB
- **Total package:** 100 MB

Larger plugins must split into separate modules (contact MSOSync support).

## Next Steps

- See [Publishing](publishing.md) to submit to the marketplace
- See [Services](services.md) for extension points coming in 14C
