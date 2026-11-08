using System.Text;
using FlowUI.Models;
using FlowUI.Services;

namespace FlowUI.Tests;

public class FlowCryptoServiceTests
{
    private readonly FlowCryptoService _crypto = new();

    [Fact]
    public void GenerateSecureKey_ReturnsBase64KeyOfExpectedLength()
    {
        var defaultKey = _crypto.GenerateSecureKey();
        var decodedDefault = Convert.FromBase64String(defaultKey);
        Assert.Equal(FlowCryptoService.DefaultKeyLength, decodedDefault.Length);

        var fallbackKey = _crypto.GenerateSecureKey(-5);
        Assert.Equal(FlowCryptoService.DefaultKeyLength, Convert.FromBase64String(fallbackKey).Length);

        var customKey = _crypto.GenerateSecureKey(48);
        Assert.Equal(48, Convert.FromBase64String(customKey).Length);
    }

    [Fact]
    public void EncryptAndDecryptArmored_RoundTripsPlaintext_AndRejectsInvalidInputs()
    {
        const string plaintext = "<pipeline name=\"secret\"><flow /></pipeline>";
        const string passphrase = "correct-horse-battery-staple";

        var armored = _crypto.EncryptPayload(plaintext, passphrase);
        Assert.True(_crypto.IsEncryptedPayload(armored));
        Assert.True(_crypto.IsEncryptedContent(armored));
        Assert.StartsWith(FlowCryptoService.ArmoredPrefixV1, armored);

        var decrypted = _crypto.DecryptPayload(armored, passphrase);
        Assert.Equal(plaintext, decrypted);

        // Wrong passphrase throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => _crypto.DecryptArmored(armored, "wrong-passphrase"));

        // Empty or invalid inputs throw
        Assert.Throws<ArgumentException>(() => _crypto.EncryptArmored(plaintext, "   "));
        Assert.Throws<ArgumentException>(() => _crypto.DecryptArmored("", passphrase));
        Assert.Throws<ArgumentException>(() => _crypto.DecryptArmored(armored, ""));
        Assert.Throws<InvalidOperationException>(() => _crypto.DecryptArmored("NOT_ARMORED", passphrase));
        Assert.Throws<InvalidOperationException>(() => _crypto.DecryptBytes(new byte[10], passphrase));
    }

    [Fact]
    public void SignatureEnvelopes_WrapAndUnwrap_AndInspectEnvelope_WorkCorrectly()
    {
        var sigBytes = Encoding.UTF8.GetBytes("fake-signature");
        var contentBytes = Encoding.UTF8.GetBytes("<pipeline />");

        var detached = FlowCryptoService.WrapSignatureOnly("openssl", sigBytes);
        Assert.True(_crypto.IsDetachedSignature(detached));
        Assert.True(FlowCryptoService.TryUnwrapSignatureOnly(detached, out var detFmt, out var unwrappedSig));
        Assert.Equal("openssl", detFmt);
        Assert.Equal(sigBytes, unwrappedSig);

        var detInspect = _crypto.InspectEnvelope(detached);
        Assert.True(detInspect.IsDetachedSignature);
        Assert.Equal("openssl", detInspect.SignatureFormat);

        var signedEnv = FlowCryptoService.WrapSignedPayload("openssl", sigBytes, contentBytes);
        Assert.True(_crypto.IsSignedPayload(signedEnv));
        Assert.True(_crypto.IsSignedEnvelope(signedEnv));
        Assert.True(FlowCryptoService.TryUnwrapSignedPayload(signedEnv, out var envFmt, out var innerText, out var envSig));
        Assert.Equal("openssl", envFmt);
        Assert.Equal("<pipeline />", innerText);
        Assert.Equal(sigBytes, envSig);

        var signedInspect = _crypto.InspectEnvelope(signedEnv);
        Assert.True(signedInspect.IsSigned);
        Assert.False(signedInspect.IsEncrypted);
        Assert.Equal("signed", signedInspect.CompositionOrder);
        Assert.Equal("<pipeline />", signedInspect.UnwrappedContent);

        // Encrypt-then-Sign inspection
        var encInner = _crypto.EncryptArmored("<pipeline />", "mykey");
        var encThenSign = FlowCryptoService.WrapSignedPayload("openssl", sigBytes, Encoding.UTF8.GetBytes(encInner));
        var etsInspect = _crypto.InspectEnvelope(encThenSign);
        Assert.True(etsInspect.IsSigned);
        Assert.True(etsInspect.IsEncrypted);
        Assert.Equal("encrypt-then-sign", etsInspect.CompositionOrder);
    }

    [Theory]
    [InlineData("AES-256-GCM")]
    [InlineData("RSA-2048")]
    [InlineData("ECDSA-P256")]
    [InlineData("ECDSA-P384")]
    public void GenerateKeyPairLocal_GeneratesKeysAndSelfSignedCertificates(string algorithm)
    {
        var resp = _crypto.GenerateKeyPairLocal(algorithm, "Test Signer");
        if (algorithm == "AES-256-GCM")
        {
            Assert.Equal("AES-256-GCM", resp.Algorithm);
            Assert.NotEmpty(resp.SecureKey);
        }
        else
        {
            Assert.Equal(algorithm, resp.Algorithm);
            Assert.Contains("PRIVATE KEY", resp.PrivateKeyPem);
            Assert.Contains("PUBLIC KEY", resp.PublicKeyPem);
            Assert.Contains("BEGIN CERTIFICATE", resp.CertificatePem);
            Assert.NotEmpty(resp.Fingerprint);
        }
    }

    [Fact]
    public void SignAndVerifyLocalOpenSsl_RsaAndEcdsa_WithEncryptionModes_WorkEndToEnd()
    {
        const string xml = "<pipeline name=\"signed_pipe\"><flow /></pipeline>";

        // 1. RSA sign + verify with certificate and public key
        var rsaKeys = _crypto.GenerateKeyPairLocal("RSA-2048", "RSA Signer");
        var rsaSigned = _crypto.SignLocalOpenSsl(new CryptoSignRequest
        {
            Content = xml,
            PrivateKeyPem = rsaKeys.PrivateKeyPem,
            Wrap = true,
            EncryptMode = "none"
        });
        Assert.True(rsaSigned.Signed);
        Assert.False(rsaSigned.Encrypted);

        var rsaVerifiedByCert = _crypto.VerifyLocalOpenSsl(new CryptoVerifyRequest
        {
            Content = rsaSigned.SignedPayload,
            CertPem = rsaKeys.CertificatePem
        });
        Assert.True(rsaVerifiedByCert.Valid);
        Assert.Equal(xml, rsaVerifiedByCert.UnwrappedContent);
        Assert.Contains("RSA Signer", rsaVerifiedByCert.Subject);

        var rsaVerifiedByPub = _crypto.VerifyLocalOpenSsl(new CryptoVerifyRequest
        {
            Content = rsaSigned.SignedPayload,
            PublicKeyPem = rsaKeys.PublicKeyPem
        });
        Assert.True(rsaVerifiedByPub.Valid);

        // 2. ECDSA sign-then-encrypt + verify
        var ecKeys = _crypto.GenerateKeyPairLocal("ECDSA-P256", "EC Signer");
        var steSigned = _crypto.SignLocalOpenSsl(new CryptoSignRequest
        {
            Content = xml,
            PrivateKeyPem = ecKeys.PrivateKeyPem,
            Wrap = true,
            EncryptMode = "sign-then-encrypt",
            SecureKey = "super-secret-pass"
        });
        Assert.True(steSigned.Signed);
        Assert.True(steSigned.Encrypted);
        Assert.True(_crypto.IsEncryptedPayload(steSigned.SignedPayload));

        var steVerified = _crypto.VerifyLocalOpenSsl(new CryptoVerifyRequest
        {
            Content = steSigned.SignedPayload,
            PublicKeyPem = ecKeys.PublicKeyPem,
            SecureKey = "super-secret-pass"
        });
        Assert.True(steVerified.Valid);
        Assert.True(steVerified.Encrypted);
        Assert.Equal(xml, steVerified.UnwrappedContent);

        // 3. Detached signature verification
        var detachedVerified = _crypto.VerifyLocalOpenSsl(new CryptoVerifyRequest
        {
            Content = xml,
            Signature = rsaSigned.DetachedSignature,
            PublicKeyPem = rsaKeys.PublicKeyPem
        });
        Assert.True(detachedVerified.Valid);

        // 4. Tampered content fails verification
        var tamperedVerify = _crypto.VerifyLocalOpenSsl(new CryptoVerifyRequest
        {
            Content = "<pipeline name=\"tampered\" />",
            Signature = rsaSigned.DetachedSignature,
            PublicKeyPem = rsaKeys.PublicKeyPem
        });
        Assert.False(tamperedVerify.Valid);
    }
}
