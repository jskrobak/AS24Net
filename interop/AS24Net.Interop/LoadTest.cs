using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AS24Net.Core.As2;
using AS24Net.Core.Security;

namespace AS24Net.Interop;

public sealed record LoadOptions(int Messages, int Concurrency, int Size, InteropCase Case);

/// <summary>
/// Load on AS24Net with real AS2 messages, secured as the case says:
/// <list type="bullet">
/// <item><b>inbound</b>: the test posts messages to <c>/as2</c> from many connections at once and verifies every
/// synchronous MDN (signature and MIC);</item>
/// <item><b>outbound</b>: the test queues messages through the REST API and receives them itself, as the partner
/// <c>LOAD</c> on the host, answering with a synchronous MDN, until all are delivered.</item>
/// </list>
/// </summary>
public sealed class LoadTest(Harness harness, LoadOptions options)
{
    public const string Partner = "LOAD";
    private const int SinkPort = 18099;

    private readonly X509Certificate2 _ours = TestCertificates.Load(harness.Certs, "load");
    private readonly X509Certificate2 _as24net = TestCertificates.LoadPublic(harness.Certs, "as24net");

    private byte[] NewPayload()
    {
        var payload = new byte[options.Size];
        RandomNumberGenerator.Fill(payload);
        return payload;
    }

    #region Inbound

    public async Task RunInboundAsync()
    {
        if (options.Case.Mdn == MdnKind.Async)
            throw new InvalidOperationException("The inbound load test needs a synchronous MDN or none, not an asynchronous one.");

        await harness.UpAsync([]);
        await harness.ConfigurePartnerAsync(Partner, $"http://host.docker.internal:{SinkPort}/as2", "load", options.Case);

        using var client = new HttpClient(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = options.Concurrency,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        }) { BaseAddress = Harness.BaseUrl, Timeout = TimeSpan.FromMinutes(2) };

        Console.WriteLine($"Warming up ...");
        for (var i = 0; i < Math.Min(20, options.Messages); i++)
            await SendAsync(client);

        Console.WriteLine($"Inbound: {options.Messages} messages of {options.Size:N0} bytes, {options.Concurrency} at a time, case {options.Case} ...");
        var latencies = new ConcurrentBag<double>();
        var errors = new ConcurrentDictionary<string, int>();
        var next = 0;
        var stopwatch = Stopwatch.StartNew();
        var progress = StartProgress(() => latencies.Count + errors.Values.Sum());

        await Task.WhenAll(Enumerable.Range(0, options.Concurrency).Select(async _ =>
        {
            while (Interlocked.Increment(ref next) <= options.Messages)
            {
                var started = Stopwatch.GetTimestamp();
                var error = await SendAsync(client);
                if (error is null)
                    latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                else
                    errors.AddOrUpdate(error, 1, (_, n) => n + 1);
            }
        }));

        var elapsed = stopwatch.Elapsed;
        progress.Cancel();
        Summary("Inbound", latencies, errors, elapsed);
    }

    /// <summary>Posts one message and checks the MDN; the error, or null.</summary>
    private async Task<string?> SendAsync(HttpClient client)
    {
        var c = options.Case;
        var message = As2MessageBuilder.Build(new As2OutboundOptions
        {
            From = Partner,
            To = Harness.Identity,
            Payload = NewPayload(),
            ContentType = "application/octet-stream",
            FileName = "load.bin",
            Compress = c.Compress,
            CompressBeforeSigning = c.CompressBeforeSigning,
            SigningCertificate = c.Sign ? _ours : null,
            SignatureAlgorithm = c.Digest,
            EncryptionCertificate = c.Encrypt ? _as24net : null,
            EncryptionAlgorithm = c.Encryption,
            Mdn = c.Mdn == MdnKind.None
                ? null
                : new MdnRequest
                {
                    NotificationTo = "load@as24net.interop",
                    Signed = c.SignedMdn,
                    MicAlgorithms = [c.MdnDigest],
                },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "as2") { Content = new ByteArrayContent(message.Body) };
        foreach (var (name, value) in message.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsByteArrayAsync();
            if (response.StatusCode != HttpStatusCode.OK)
                return $"HTTP {(int)response.StatusCode}";
            if (c.Mdn == MdnKind.None)
                return null;

            var headers = As2Headers.NewCollection();
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                headers[header.Key] = string.Join(", ", header.Value);
            var mdn = MdnProcessor.Read(headers, body, [_as24net], requireSignature: c.SignedMdn);
            if (!mdn.Disposition.EndsWith("processed", StringComparison.OrdinalIgnoreCase))
                return $"MDN: {mdn.Disposition}";
            return Mic.AreEqual(mdn.ReceivedContentMic, message.Mic) ? null : "MDN: a different MIC";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    #endregion

    #region Outbound

    public async Task RunOutboundAsync()
    {
        await harness.UpAsync([]);
        await harness.ConfigurePartnerAsync(Partner, $"http://host.docker.internal:{SinkPort}/as2", "load", options.Case);

        var received = 0;
        var sinkErrors = new ConcurrentDictionary<string, int>();
        await using var sink = StartSink(() => Interlocked.Increment(ref received), error => sinkErrors.AddOrUpdate(error, 1, (_, n) => n + 1));

        var reference = $"load-{DateTime.UtcNow:HHmmss}";
        Console.WriteLine($"Outbound: {options.Messages} messages of {options.Size:N0} bytes, case {options.Case} ...");

        // Queued through the REST API, several requests at a time, as an application would.
        var queueErrors = new ConcurrentDictionary<string, int>();
        var next = 0;
        var stopwatch = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, Math.Min(options.Concurrency, 16)).Select(async _ =>
        {
            while (Interlocked.Increment(ref next) <= options.Messages)
            {
                try
                {
                    await harness.QueueAsync(Partner, NewPayload(), $"load-{next}.bin", reference);
                }
                catch (Exception ex)
                {
                    queueErrors.AddOrUpdate(ex.Message.Split('\n')[0], 1, (_, n) => n + 1);
                }
            }
        }));
        var queued = stopwatch.Elapsed;
        Console.WriteLine($"  queued in {queued.TotalSeconds:F1} s ({options.Messages / queued.TotalSeconds:F0} messages/s through the API)");

        // Delivered: counted by the status of the messages of this run.
        int delivered = 0, failed = 0;
        var lastChange = DateTime.UtcNow;
        while (delivered + failed < options.Messages - queueErrors.Values.Sum())
        {
            await Task.Delay(1000);
            var nowDelivered = await CountAsync("Delivered", reference);
            failed = await CountAsync("Failed", reference) + await CountAsync("NotDelivered", reference);
            if (nowDelivered != delivered)
                lastChange = DateTime.UtcNow;
            delivered = nowDelivered;
            Console.Write($"\r  delivered {delivered}, failed {failed}, received by the partner {received}   ");
            if (DateTime.UtcNow - lastChange > TimeSpan.FromMinutes(2))
            {
                Console.WriteLine("\n  Nothing was delivered for two minutes, giving up.");
                break;
            }
        }

        var elapsed = stopwatch.Elapsed;
        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("Outbound");
        Console.WriteLine($"  messages       {options.Messages}, delivered {delivered}, failed {failed}");
        Console.WriteLine($"  time           {elapsed.TotalSeconds:F1} s from the first queued to the last delivered");
        Console.WriteLine($"  throughput     {delivered / elapsed.TotalSeconds:F1} messages/s, {delivered * (double)options.Size / elapsed.TotalSeconds / 1024 / 1024:F2} MB/s");
        foreach (var (error, count) in queueErrors.Concat(sinkErrors).OrderByDescending(e => e.Value))
            Console.WriteLine($"  error ×{count,-6} {error}");
    }

    private async Task<int> CountAsync(string status, string reference)
    {
        var page = await harness.ReadAsync(await harness.Api.GetAsync($"messages?partner={Partner}&status={status}&reference={reference}&take=1"));
        return page["totalCount"]!.GetValue<int>();
    }

    /// <summary>The partner LOAD: opens every message of AS24Net and answers with a synchronous MDN.</summary>
    private WebApplication StartSink(Action onReceived, Action<string> onError)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{SinkPort}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = null);
        var app = builder.Build();
        app.MapPost("/as2", async (HttpContext context) =>
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer);
            var headers = As2Headers.NewCollection();
            foreach (var header in context.Request.Headers)
                headers[header.Key] = header.Value.ToString();

            var request = MdnRequest.FromHeaders(headers);
            string disposition = Mdn.ProcessedDisposition, mic = "";
            try
            {
                var message = As2MessageReader.Read(headers, buffer.ToArray(), [_ours], [_as24net], request?.MicAlgorithm(As2Algorithms.Sha256) ?? As2Algorithms.Sha256);
                mic = message.Mic;
                onReceived();
            }
            catch (As2ProcessingException ex)
            {
                disposition = Mdn.ErrorDisposition(ex.Error);
                onError($"partner: {ex.Message}");
            }

            if (request is null)
                return Results.Ok();

            var mdn = MdnProcessor.Build(new MdnOptions
            {
                From = Partner,
                To = Harness.Identity,
                OriginalMessageId = headers.GetValueOrDefault(As2Headers.MessageId) ?? "",
                Disposition = disposition,
                ReceivedContentMic = mic,
                SigningCertificate = request.Signed ? _ours : null,
                SignatureAlgorithm = request.MicAlgorithm(As2Algorithms.Sha256),
            });
            foreach (var (name, value) in mdn.Headers)
            {
                if (!name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    context.Response.Headers[name] = value;
            }

            return Results.Bytes(mdn.Body, mdn.Headers[As2Headers.ContentType]);
        });
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    #endregion

    private static CancellationTokenSource StartProgress(Func<int> done)
    {
        var cancellation = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(1000);
                Console.Write($"\r  {done()} done   ");
            }
        });
        return cancellation;
    }

    private void Summary(string title, ConcurrentBag<double> latencies, ConcurrentDictionary<string, int> errors, TimeSpan elapsed)
    {
        var sorted = latencies.Order().ToArray();
        double Percentile(double p) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine($"  messages       {options.Messages}, ok {sorted.Length}, errors {errors.Values.Sum()}");
        Console.WriteLine($"  time           {elapsed.TotalSeconds:F1} s");
        Console.WriteLine($"  throughput     {sorted.Length / elapsed.TotalSeconds:F1} messages/s, {sorted.Length * (double)options.Size / elapsed.TotalSeconds / 1024 / 1024:F2} MB/s");
        Console.WriteLine($"  latency (ms)   p50 {Percentile(0.5):F0}, p90 {Percentile(0.9):F0}, p99 {Percentile(0.99):F0}, max {(sorted.Length == 0 ? 0 : sorted[^1]):F0}");
        foreach (var (error, count) in errors.OrderByDescending(e => e.Value))
            Console.WriteLine($"  error ×{count,-6} {error}");
    }
}
