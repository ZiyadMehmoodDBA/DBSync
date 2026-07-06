using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Audit;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.NodeManagement;

public sealed class ProvisionPackageService(AppDbContext db, IAuditService auditSvc) : IProvisionPackageService
{
    private const string AgentVersion = "1.0.0";

    public async Task StreamPackageAsync(
        string nodeId, string token, string actorUsername, Stream destination, CancellationToken ct = default)
    {
        var node = await db.Nodes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.NodeId == nodeId, ct)
            ?? throw new NotFoundException($"Node '{nodeId}' not found.");

        var provision = new ProvisionResultDto(nodeId, token);

        var sw = Stopwatch.StartNew();
        try
        {
            // Collect all file contents first so we can compute checksums
            var files = new Dictionary<string, byte[]>
            {
                ["msosync-node.json"] = BuildNodeConfig(provision, node),
                [".env.example"]      = BuildEnvExample(provision),
                ["README.md"]         = BuildReadme(provision),
            };

            // Build manifest with file count (excluding checksums.txt itself)
            files["manifest.json"] = BuildManifest(provision, files.Count + 1);

            // Build checksums over the 4 content files
            var checksums = new StringBuilder();
            foreach (var (name, content) in files)
            {
                var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                checksums.AppendLine($"{hash}  {name}");
            }
            files["checksums.txt"] = Encoding.UTF8.GetBytes(checksums.ToString());

            // Build the ZIP in a MemoryStream first so ZipArchive.Dispose() flushes the
            // central-directory synchronously without touching the HTTP response stream
            // (ASP.NET Core TestHost and Kestrel with default settings disallow synchronous writes).
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in files)
                {
                    var entry  = zip.CreateEntry(name, CompressionLevel.Fastest);
                    using var s = entry.Open();
                    s.Write(content);
                }
            }  // zip.Dispose() finalises the central-directory into ms (all in-memory, sync is fine)

            ms.Position = 0;
            await ms.CopyToAsync(destination, ct);

            // Audit: never include the token value in the audit detail
            await auditSvc.WriteAsync(
                NodeManagementAuditActions.ProvisionPackageDownloaded,
                $"nodeId={nodeId}",
                actorUsername,
                ct);

            NodeManagementMetrics.PackageDownloadsTotal.Add(1);
        }
        finally
        {
            NodeManagementMetrics.PackageGenerationDuration.Record(sw.Elapsed.TotalSeconds);
        }
    }

    private static byte[] BuildNodeConfig(ProvisionResultDto p, SyncNode n)
    {
        var obj = new
        {
            nodeId     = p.NodeId,
            externalId = n.ExternalId,
            name       = n.NodeName,
            type       = n.NodeType,
            groupId    = n.GroupId,
            serverUrl  = n.SyncUrl,
            created    = DateTime.UtcNow.ToString("o"),
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static byte[] BuildEnvExample(ProvisionResultDto p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MSOSync Node Environment Variables");
        sb.AppendLine("# Replace <values> with actual credentials");
        sb.AppendLine("MSOSYNC_NODE_TOKEN=<your-token-here>");
        sb.AppendLine($"MSOSYNC_NODE_ID={p.NodeId}");
        sb.AppendLine("MSOSYNC_DB_SERVER=<db-server>");
        sb.AppendLine("MSOSYNC_DB_NAME=<db-name>");
        sb.AppendLine("MSOSYNC_DB_USER=<db-user>");
        sb.AppendLine("MSOSYNC_DB_PASSWORD=<db-password>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildReadme(ProvisionResultDto p)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# MSOSync Node: {p.NodeId}");
        sb.AppendLine();
        sb.AppendLine("## Setup");
        sb.AppendLine("1. Copy `.env.example` to `.env` and fill in your credentials.");
        sb.AppendLine("2. Set `MSOSYNC_NODE_TOKEN` to the token provided at provisioning time.");
        sb.AppendLine("3. Start the MSOSync node agent.");
        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine("- `msosync-node.json` — node configuration");
        sb.AppendLine("- `.env.example` — environment variable template");
        sb.AppendLine("- `manifest.json` — package metadata");
        sb.AppendLine("- `checksums.txt` — SHA-256 hashes for integrity verification");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildManifest(ProvisionResultDto p, int fileCount)
    {
        var obj = new
        {
            nodeId       = p.NodeId,
            agentVersion = AgentVersion,
            generatedAt  = DateTime.UtcNow.ToString("o"),
            fileCount,
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
