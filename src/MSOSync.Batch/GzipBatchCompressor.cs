using System.IO.Compression;

namespace MSOSync.Batch;

public sealed class GzipBatchCompressor
{
    public byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal))
            gz.Write(data);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var output = new MemoryStream();
        using (var gz = new GZipStream(input, CompressionMode.Decompress))
            gz.CopyTo(output);
        return output.ToArray();
    }
}
