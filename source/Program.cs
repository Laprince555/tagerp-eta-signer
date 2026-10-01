using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using TagErp.EtaSigner;

// TagERP ETA signing bridge. Run it on the PC the ETA USB token is plugged into:
//   1. fetches the invoices waiting for a signature from TagERP,
//   2. signs them with the token (its driver asks for the PIN once per run),
//   3. posts the signatures back and exits.
// Nothing listens on this PC; it only makes outgoing HTTPS calls.

if (args.Contains("--self-test"))
{
    return SelfTest();
}

if (args.Contains("--token-test"))
{
    return TokenTest(args.SkipWhile(a => a != "--thumbprint").Skip(1).FirstOrDefault());
}

var settings = Settings.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl.TrimEnd('/') + "/") };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.SignerKey);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

try
{
    var pending = await http.GetFromJsonAsync<PendingResponse>("api/e-invoice/eta-signer/pending", json)
        ?? new PendingResponse([]);

    if (pending.Documents.Count == 0)
    {
        Console.WriteLine("No invoices are waiting for a signature. / لا توجد فواتير في انتظار التوقيع.");
        return Pause(0);
    }

    Console.WriteLine($"{pending.Documents.Count} invoice(s) to sign. / عدد الفواتير: {pending.Documents.Count}");

    using var certificate = Certificates.Find(settings.CertificateThumbprint);
    Console.WriteLine($"Signing with: {certificate.Subject}");

    var signatures = new List<SignatureItem>();

    foreach (var document in pending.Documents)
    {
        signatures.Add(new SignatureItem(document.Id, CadesSigner.Sign(document.Canonical, certificate)));
        Console.WriteLine($"  signed {document.InternalId}");
    }

    var response = await http.PostAsJsonAsync("api/e-invoice/eta-signer/signatures", new SignaturesRequest(signatures), json);
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<SignaturesResponse>(json);

    Console.WriteLine($"Sent {result?.Accepted ?? 0} signature(s) to TagERP. / تم إرسال التوقيعات.");
    return Pause(0);
}
catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
{
    Console.Error.WriteLine("TagERP rejected the signing key: check SignerKey in appsettings.json. / مفتاح برنامج التوقيع غير صحيح.");
    return Pause(2);
}
catch (CryptographicException exception)
{
    Console.Error.WriteLine($"The token could not sign (wrong PIN or token not plugged in?): {exception.Message}");
    return Pause(3);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Signing failed: {exception.Message}");
    return Pause(1);
}

// Verifies the CAdES-BES output with a throwaway software certificate — no token needed.
static int SelfTest()
{
    using var key = RSA.Create(2048);
    var request = new CertificateRequest("CN=TagERP Self Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

    const string canonical = "\"ISSUER\"\"NAME\"\"Nile\"";
    var signature = CadesSigner.Sign(canonical, certificate);

    var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.7.5"), Encoding.UTF8.GetBytes(canonical)), detached: true);
    cms.Decode(Convert.FromBase64String(signature));
    cms.CheckSignature(verifySignatureOnly: true);

    var attributes = cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().Select(a => a.Oid.Value).ToList();

    if (!attributes.Contains("1.2.840.113549.1.9.16.2.47") || !attributes.Contains("1.2.840.113549.1.9.5"))
    {
        Console.Error.WriteLine("Self-test FAILED: signing-certificate-v2 or signing-time attribute missing.");
        return 1;
    }

    Console.WriteLine("Self-test passed: CAdES-BES signature verifies and carries signing time + signing-certificate-v2.");
    return 0;
}

// Signs a sample canonical string with the real USB token (the driver asks for the PIN) and
// verifies the signature locally. Sends nothing anywhere.
static int TokenTest(string? thumbprint)
{
    try
    {
        using var certificate = Certificates.Find(thumbprint);
        Console.WriteLine($"Certificate: {certificate.Subject}");
        Console.WriteLine($"Issuer:      {certificate.Issuer}");

        const string canonical = "\"ISSUER\"\"NAME\"\"Token Test\"";
        var signature = CadesSigner.Sign(canonical, certificate);

        var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.7.5"), Encoding.UTF8.GetBytes(canonical)), detached: true);
        cms.Decode(Convert.FromBase64String(signature));
        cms.CheckSignature(verifySignatureOnly: true);

        Console.WriteLine($"Token test passed: signed with the token and the signature verifies ({signature.Length} base64 chars).");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Token test FAILED: {exception.Message}");
        return 1;
    }
}

static int Pause(int exitCode)
{
    if (!Console.IsInputRedirected)
    {
        Console.WriteLine("Press Enter to close. / اضغط Enter للإغلاق.");
        Console.ReadLine();
    }

    return exitCode;
}

internal sealed record Settings(string ServerUrl, string SignerKey, string? CertificateThumbprint)
{
    public static Settings Load(string path)
    {
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("appsettings.json is empty.");

        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.SignerKey))
        {
            throw new InvalidOperationException("Set ServerUrl and SignerKey in appsettings.json.");
        }

        return settings;
    }
}

internal static class Certificates
{
    /// <summary>
    /// The token's certificate as the driver publishes it in the current user's store: the
    /// configured thumbprint, or else the single valid certificate with a private key.
    /// </summary>
    public static X509Certificate2 Find(string? thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var candidates = store.Certificates
            .Where(c => c.HasPrivateKey && c.NotAfter > DateTime.Now && c.NotBefore < DateTime.Now)
            .Where(c => string.IsNullOrWhiteSpace(thumbprint) || string.Equals(c.Thumbprint, thumbprint.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A PC usually holds other certificates with private keys (dev certs, device certs);
        // without a configured thumbprint, keep the ETA-issued ones: their subject carries the
        // taxpayer's organization identifier "VATEG-<tax registration>".
        if (string.IsNullOrWhiteSpace(thumbprint) && candidates.Count > 1)
        {
            candidates = candidates.Where(c => c.Subject.Contains("VATEG-", StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException("No signing certificate found. Plug in the USB token, or set CertificateThumbprint."),
            _ => throw new InvalidOperationException("Several certificates found; set CertificateThumbprint in appsettings.json:\n  "
                + string.Join("\n  ", candidates.Select(c => $"{c.Thumbprint}  {c.Subject}"))),
        };
    }
}

internal sealed record PendingDocument(int Id, string? InternalId, string Canonical);

internal sealed record PendingResponse(List<PendingDocument> Documents);

internal sealed record SignatureItem(int Id, string Signature);

internal sealed record SignaturesRequest(List<SignatureItem> Signatures);

internal sealed record SignaturesResponse(int Accepted);
