using System.IO.Compression;

namespace MSOSync.Transport;

public sealed class GzipCompressionService
{
    public byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var gzip   = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
