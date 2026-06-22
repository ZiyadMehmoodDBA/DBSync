using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MSOSync.Transport;

public sealed class TransportFailureClassifier : ITransportFailureClassifier
{
    public TransportFailureReason Classify(Exception ex) => ex switch
    {
        TaskCanceledException                                                  => TransportFailureReason.Timeout,
        OperationCanceledException                                             => TransportFailureReason.Timeout,
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }       => TransportFailureReason.AuthenticationFailure,
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden }          => TransportFailureReason.AuthenticationFailure,
        HttpRequestException                                                    => TransportFailureReason.ConnectionRefused,
        InvalidDataException                                                    => TransportFailureReason.CompressionFailure,
        JsonException                                                           => TransportFailureReason.CompressionFailure,
        _                                                                       => TransportFailureReason.Unknown
    };
}
