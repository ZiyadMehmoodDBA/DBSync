namespace MSOSync.Transport;

public enum TransportFailureReason
{
    Timeout               = 0,
    HttpError             = 1,
    ConnectionRefused     = 2,
    CompressionFailure    = 3,
    SequenceGap           = 4,
    ApplyFailure          = 5,
    AuthenticationFailure = 6,
    Unknown               = 7
}
