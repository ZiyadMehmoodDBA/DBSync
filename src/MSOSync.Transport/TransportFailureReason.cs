namespace MSOSync.Transport;

public enum TransportFailureReason
{
    Timeout,
    HttpError,
    ConnectionRefused,
    CompressionFailure,
    SequenceGap,
    ApplyFailure,
    AuthenticationFailure,
    Unknown
}
