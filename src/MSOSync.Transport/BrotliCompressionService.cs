using System.IO.Compression;
using Microsoft.Extensions.Options;

namespace MSOSync.Transport;

public sealed class BrotliCompressionService : ICompressionService
{
    private readonly CompressionLevel _level;

    public BrotliCompressionService(IOptions<CompressionOptions> options)
        => _level = MapLevel(options.Value.Level);

    public string EncodingName => "br";

    public byte[] Compress(byte[] data)
    {
        using var output  = new MemoryStream();
        using (var brotli = new BrotliStream(output, _level, leaveOpen: true))
            brotli.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public byte[] Decompress(byte[] data)
    {
        using var input  = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static CompressionLevel MapLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Fastest      => CompressionLevel.Fastest,
        CompressionLevelOption.SmallestSize => CompressionLevel.SmallestSize,
        _                                   => CompressionLevel.Optimal
    };
}
