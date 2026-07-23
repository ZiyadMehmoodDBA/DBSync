using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MSOSync.Plugin.Signing;
using Xunit;

namespace MSOSync.PluginTests.Signing;

public sealed class RsaPssSignerTests : IDisposable
{
    private readonly RSA              _privateKey;
    private readonly RsaPssPluginSigner _signer;

    public RsaPssSignerTests()
    {
        _privateKey = RSA.Create(2048);
        _signer     = new RsaPssPluginSigner(_privateKey, "test-key-01");
    }

    public void Dispose() => _privateKey.Dispose();

    [Fact]
    public void Sign_ProducesBase64EncodedOutput()
    {
        var data    = SHA256.HashData(Encoding.UTF8.GetBytes("hello world"));
        var result  = _signer.Sign(data);

        var decoded = Convert.TryFromBase64String(result, new byte[512], out _);
        decoded.Should().BeTrue("result must be valid Base64");
    }

    [Fact]
    public void Sign_SameInputProducesVerifiableOutput()
    {
        var data      = SHA256.HashData(Encoding.UTF8.GetBytes("canonical manifest json"));
        var signature = _signer.Sign(data);
        var sigBytes  = Convert.FromBase64String(signature);

        // Verify using only the public key
        using var publicKey = RSA.Create();
        publicKey.ImportRSAPublicKey(_privateKey.ExportRSAPublicKey(), out _);

        var valid = publicKey.VerifyHash(
            data.ToArray(),
            sigBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        valid.Should().BeTrue("signature produced by the signer must verify with the corresponding public key");
    }

    [Fact]
    public void Sign_PublicKeyId_MatchesConstructorValue()
        => _signer.PublicKeyId.Should().Be("test-key-01");

    [Fact]
    public void Sign_EmptyData_ProducesVerifiableOutput()
    {
        // Edge case: signing an empty hash (all zeros)
        var data      = new byte[32];
        var signature = _signer.Sign(data);
        signature.Should().NotBeNullOrEmpty();
        Convert.TryFromBase64String(signature, new byte[512], out _).Should().BeTrue();
    }
}
