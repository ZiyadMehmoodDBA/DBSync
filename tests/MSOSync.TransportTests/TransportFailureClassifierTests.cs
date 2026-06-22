using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class TransportFailureClassifierTests
{
    private static readonly TransportFailureClassifier Classifier = new();

    [Fact]
    public void Classify_TaskCanceledException_ReturnsTimeout()
        => Classifier.Classify(new TaskCanceledException())
            .Should().Be(TransportFailureReason.Timeout);

    [Fact]
    public void Classify_HttpRequestException_401_ReturnsAuthenticationFailure()
        => Classifier.Classify(new HttpRequestException(null, null, HttpStatusCode.Unauthorized))
            .Should().Be(TransportFailureReason.AuthenticationFailure);

    [Fact]
    public void Classify_HttpRequestException_403_ReturnsAuthenticationFailure()
        => Classifier.Classify(new HttpRequestException(null, null, HttpStatusCode.Forbidden))
            .Should().Be(TransportFailureReason.AuthenticationFailure);

    [Fact]
    public void Classify_HttpRequestException_503_ReturnsConnectionRefused()
        => Classifier.Classify(new HttpRequestException(null, null, HttpStatusCode.ServiceUnavailable))
            .Should().Be(TransportFailureReason.ConnectionRefused);

    [Fact]
    public void Classify_JsonException_ReturnsCompressionFailure()
        => Classifier.Classify(new JsonException("bad json"))
            .Should().Be(TransportFailureReason.CompressionFailure);

    [Fact]
    public void Classify_InvalidDataException_ReturnsCompressionFailure()
        => Classifier.Classify(new InvalidDataException("bad gzip"))
            .Should().Be(TransportFailureReason.CompressionFailure);

    [Fact]
    public void Classify_ArbitraryException_ReturnsUnknown()
        => Classifier.Classify(new Exception("unknown"))
            .Should().Be(TransportFailureReason.Unknown);
}
