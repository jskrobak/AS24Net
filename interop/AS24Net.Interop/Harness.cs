using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AS24Net.Interop;

/// <summary>
/// The test environment: the Docker Compose project in <c>interop/</c> (AS24Net with PostgreSQL and the other AS2
/// stations), the files it needs in <c>interop/work</c>, and the REST API of AS24Net.
/// </summary>
public sealed class Harness : IDisposable
{
    public const string ProjectName = "as24net-interop";

    /// <summary>API token put straight into the database: the environment is thrown away after the tests.</summary>
    public const string Token = "a24_interop-tests-only-not-a-secret";

    /// <summary>Our AS2 name in every test.</summary>
    public const string Identity = "AS24NET";

    /// <summary>The AS2 endpoint of AS24Net as the other stations reach it.</summary>
    public const string As2UrlInside = "http://as24net:8080/as2";

    public static readonly Uri BaseUrl = new(Environment.GetEnvironmentVariable("AS24NET_URL") ?? "http://localhost:18080/");

    public string Root { get; }
    public string Work => Path.Combine(Root, "work");
    public string Certs => Path.Combine(Work, "certs");
    public HttpClient Api { get; }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Harness()
    {
        Root = FindRoot();
        Api = new HttpClient { BaseAddress = new Uri(BaseUrl, "api/v1/"), Timeout = TimeSpan.FromMinutes(2) };
        Api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "interop", "docker-compose.yml")))
                return Path.Combine(directory.FullName, "interop");
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")) && directory.Name == "interop")
                return directory.FullName;
        }

        throw new InvalidOperationException("Run the tool from the repository (interop/docker-compose.yml was not found).");
    }

    #region Docker Compose

    public async Task<string> ComposeAsync(string arguments, bool check = true)
    {
        var (exitCode, output) = await RunAsync("docker", $"compose --project-name {ProjectName} -f \"{Path.Combine(Root, "docker-compose.yml")}\" {arguments}");
        if (check && exitCode != 0)
            throw new InvalidOperationException($"docker compose {arguments} failed ({exitCode}):\n{output}");
        return output;
    }

    public static async Task<(int ExitCode, string Output)> RunAsync(string file, string arguments, string? input = null)
    {
        var start = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
        };
        using var process = Process.Start(start)!;
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await output + await error).Trim());
    }

    /// <summary>
    /// Starts AS24Net and the given services, builds what changed and waits until they are healthy; then gives
    /// AS24Net the API token and the settings the tests need and restarts it, as it reads the settings once.
    /// </summary>
    public async Task UpAsync(IEnumerable<string> services)
    {
        TestCertificates.EnsureCreated(Certs);
        var list = string.Join(' ', new[] { "postgres", "as24net" }.Concat(services).Distinct());
        Console.WriteLine($"Starting {list} (the first build takes a few minutes) ...");
        await ComposeAsync($"up -d --build --wait --wait-timeout 600 {list}");

        await SqlAsync($"""
            INSERT INTO "ApiTokens" ("Name", "Prefix", "TokenHash", "Enabled", "AllowConfiguration", "Created")
            SELECT 'interop', '{Token[..12]}', '{Hash(Token)}', true, true, now()
            WHERE NOT EXISTS (SELECT 1 FROM "ApiTokens" WHERE "TokenHash" = '{Hash(Token)}');
            """);
        var settings = new Dictionary<string, object>
        {
            ["PublicUrl"] = As2UrlInside,
            ["SendIntervalSeconds"] = 5,
            ["MaxParallelSends"] = 16,
            ["MaxRetryCount"] = 1,
            ["RetryDelayMinutes"] = 1,
        };
        await SqlAsync(string.Join("\n", settings.Select(s => $"""
            INSERT INTO "GlobalSettings" ("Name", "Json") VALUES ('{s.Key}', '{JsonSerializer.Serialize(s.Value)}')
            ON CONFLICT ("Name") DO UPDATE SET "Json" = EXCLUDED."Json";
            """)));
        await ComposeAsync("restart as24net");
        await ComposeAsync("up -d --wait --wait-timeout 300 as24net");
        await ConfigureIdentityAsync(Identity);
    }

    public Task<string> SqlAsync(string sql) =>
        RunAsync("docker", $"compose --project-name {ProjectName} -f \"{Path.Combine(Root, "docker-compose.yml")}\" " +
                           "exec -T postgres psql -v ON_ERROR_STOP=1 -U as24net -d as24net", sql)
            .ContinueWith(t => t.Result.ExitCode == 0 ? t.Result.Output : throw new InvalidOperationException($"SQL failed: {t.Result.Output}"));

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    #endregion

    #region AS24Net configuration

    private readonly Dictionary<string, int> _certificateIds = [];

    /// <summary>Stores a certificate of <c>work/certs</c> (with the private key or without) and returns its id.</summary>
    public async Task<int> CertificateIdAsync(string name, bool privateKey)
    {
        var key = name + (privateKey ? ".pfx" : ".pem");
        if (_certificateIds.TryGetValue(key, out var id))
            return id;

        var response = await Api.PostAsJsonAsync("certificates", new
        {
            fileName = key,
            data = Convert.ToBase64String(await File.ReadAllBytesAsync(Path.Combine(Certs, key))),
            password = privateKey ? TestCertificates.Password : null,
            name = $"{name} (interop)",
        }, Json);
        var certificate = await ReadAsync(response);
        return _certificateIds[key] = certificate["id"]!.GetValue<int>();
    }

    private readonly HashSet<string> _identities = [];

    /// <summary>Our identity with the AS2 name, all with the same key pair.</summary>
    private async Task ConfigureIdentityAsync(string as2Id)
    {
        if (!_identities.Add(as2Id))
            return;

        var certificate = await CertificateIdAsync("as24net", privateKey: true);
        await ReadAsync(await Api.PutAsJsonAsync($"identities/{Uri.EscapeDataString(as2Id)}", new
        {
            name = as2Id,
            signingCertificateId = certificate,
            decryptionCertificateId = certificate,
        }, Json));
    }

    /// <summary>Creates or updates the connection and the partner of a station for one case.</summary>
    public async Task ConfigurePartnerAsync(string as2Id, string url, string station, InteropCase c, string identity = Identity,
        bool unsignedWithoutMime = false)
    {
        await ConfigureIdentityAsync(identity);
        var certificate = await CertificateIdAsync(station, privateKey: false);
        await ReadAsync(await Api.PutAsJsonAsync($"connections/{Uri.EscapeDataString(as2Id)}", new
        {
            url,
            signMessages = c.Sign,
            signatureAlgorithm = c.Digest,
            encryptMessages = c.Encrypt,
            encryptionAlgorithm = c.Encryption,
            compressMessages = c.Compress,
            compressBeforeSigning = c.CompressBeforeSigning,
            unsignedWithoutMime,
            mdnMode = c.Mdn.ToString(),
            requestSignedMdn = c.SignedMdn,
            mdnTimeoutMinutes = 10,
            // What the station sends is required to be as the case says, so that a missing layer is noticed.
            requireSignedMessages = c.Sign,
            requireEncryptedMessages = c.Encrypt,
            signatureCertificateId = certificate,
            encryptionCertificateId = certificate,
            timeoutSeconds = 60,
        }, Json));
        await ReadAsync(await Api.PutAsJsonAsync($"partners/{Uri.EscapeDataString(as2Id)}", new
        {
            name = as2Id,
            connection = as2Id,
            defaultIdentity = identity,
            contentType = "application/edifact",
            enabled = true,
        }, Json));
    }

    public async Task<JsonNode> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: " +
                                                    $"HTTP {(int)response.StatusCode} {text}");
            return JsonNode.Parse(text.Length == 0 ? "{}" : text)!;
        }
    }

    /// <summary>Puts a message into the send queue of AS24Net and returns its id.</summary>
    public async Task<int> QueueAsync(string partner, byte[] payload, string fileName, string? reference = null,
        string identity = Identity)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(payload);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/edifact");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(partner), "partner");
        form.Add(new StringContent(identity), "identity");
        if (reference is not null)
            form.Add(new StringContent(reference), "reference");
        var message = await ReadAsync(await Api.PostAsync("messages", form));
        return message["id"]!.GetValue<int>();
    }

    /// <summary>Waits until an outgoing message is delivered or failed for good.</summary>
    public async Task<JsonNode> WaitForMessageAsync(int id, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var message = await ReadAsync(await Api.GetAsync($"messages/{id}"));
            var status = message["status"]!.GetValue<string>();
            // Error is retried, so it is final only with the retries used up; the case fails on the timeout then.
            if (status is "Delivered" or "Failed" or "NotDelivered" || DateTime.UtcNow > until)
                return message;
            await Task.Delay(500);
        }
    }

    /// <summary>
    /// A message received from the partner, by its Message-ID or, when the sender does not tell it, by its file
    /// name; with its payload.
    /// </summary>
    public async Task<(JsonNode? Message, byte[]? Payload)> FindReceivedAsync(string partner, string? messageId, string fileName,
        TimeSpan timeout)
    {
        var normalized = messageId?.Trim('<', '>');
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            var page = await ReadAsync(await Api.GetAsync($"inbox?partner={Uri.EscapeDataString(partner)}&take=50"));
            var message = page["items"]!.AsArray().FirstOrDefault(m => normalized is not null
                ? m!["messageId"]!.GetValue<string>().Trim('<', '>') == normalized
                // Without a Content-Disposition (OpenAS2 sends none for an unsecured message) AS24Net names the file after
                // the Message-ID, which OpenAS2 builds from the file name.
                : m!["fileName"]?.GetValue<string>() == fileName || m["messageId"]!.GetValue<string>().Contains(fileName));
            if (message is not null)
            {
                var id = message["id"]!.GetValue<int>();
                using var content = await Api.GetAsync($"inbox/{id}/content");
                return (message, content.IsSuccessStatusCode ? await content.Content.ReadAsByteArrayAsync() : null);
            }

            if (DateTime.UtcNow > until)
                return (null, null);
            await Task.Delay(500);
        }
    }

    #endregion

    public void Dispose() => Api.Dispose();
}
