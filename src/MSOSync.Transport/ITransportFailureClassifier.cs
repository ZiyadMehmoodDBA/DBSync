namespace MSOSync.Transport;

public interface ITransportFailureClassifier
{
    TransportFailureReason Classify(Exception ex);
}
