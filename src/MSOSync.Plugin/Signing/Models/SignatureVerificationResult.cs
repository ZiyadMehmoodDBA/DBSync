namespace MSOSync.Plugin.Signing.Models;

public enum SignatureVerificationOutcome
{
    Valid,
    NoSignature,
    UnknownPublisher,
    InvalidBase64,
    InvalidSignature,
    UnsupportedAlgorithm,
}

public sealed record SignatureVerificationResult(
    SignatureVerificationOutcome Outcome,
    string?                      PublicKeyId,
    string?                      ErrorMessage)
{
    public bool IsValid => Outcome == SignatureVerificationOutcome.Valid;

    public static SignatureVerificationResult Valid(string publicKeyId)
        => new(SignatureVerificationOutcome.Valid, publicKeyId, null);

    public static SignatureVerificationResult NoSignature()
        => new(SignatureVerificationOutcome.NoSignature, null, "Manifest contains no signature block.");

    public static SignatureVerificationResult UnknownPublisher(string keyId)
        => new(SignatureVerificationOutcome.UnknownPublisher, keyId,
               $"Public key ID '{keyId}' is not in the trusted publisher registry.");

    public static SignatureVerificationResult InvalidBase64(string keyId)
        => new(SignatureVerificationOutcome.InvalidBase64, keyId,
               "Signature value is not valid Base64.");

    public static SignatureVerificationResult InvalidSignature(string keyId)
        => new(SignatureVerificationOutcome.InvalidSignature, keyId,
               "Signature does not match the canonical manifest hash.");

    public static SignatureVerificationResult UnsupportedAlgorithm(string algorithm)
        => new(SignatureVerificationOutcome.UnsupportedAlgorithm, null,
               $"Signing algorithm '{algorithm}' is not supported. Expected 'RSA-PSS-SHA256'.");
}
