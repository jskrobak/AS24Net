using Microsoft.AspNetCore.Http.Features;
using AS24Net.Core.As2;
using AS24Net.Services;
using AS24Net.Services.As2;

namespace AS24Net.Server.Api;

/// <summary>
/// The AS2 endpoint (<c>POST /as2</c>) partners send messages and asynchronous MDNs to. It is public: partners are
/// recognised by their AS2 name and authenticated by the signatures of their messages.
/// </summary>
public static class As2Endpoints
{
    public const string Path = "/as2";

    public static void MapAs2(this WebApplication app)
    {
        app.MapPost(Path, HandleAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .ExcludeFromDescription();

        // A partner or a person checking the address gets an answer instead of an error page.
        app.MapGet(Path, () => Results.Text($"{As2Headers.Product} AS2 endpoint: partners POST AS2 messages and MDNs here.\n"))
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static async Task HandleAsync(HttpContext context, As2InboundService inbound, GlobalSettingsService settingsService,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetGlobalSettingsAsync();
        var maxSize = settings.MaxMessageSizeMb * 1024L * 1024;
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = maxSize;

        if (context.Request.ContentLength > maxSize)
        {
            await WriteTextAsync(context, StatusCodes.Status413PayloadTooLarge, $"Messages of up to {settings.MaxMessageSizeMb} MB are accepted.");
            return;
        }

        byte[] body;
        try
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, cancellationToken);
            body = buffer.ToArray();
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            await WriteTextAsync(context, StatusCodes.Status413PayloadTooLarge, $"Messages of up to {settings.MaxMessageSizeMb} MB are accepted.");
            return;
        }

        var headers = As2Headers.NewCollection();
        foreach (var (name, values) in context.Request.Headers)
            headers[name] = values.ToString();

        var remote = context.Connection.RemoteIpAddress?.ToString();
        As2Response response;
        try
        {
            response = await inbound.HandleAsync(headers, body, remote, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger(typeof(As2Endpoints)).LogError(ex, "Processing a request of {RemoteAddress} failed", remote);
            await WriteTextAsync(context, StatusCodes.Status500InternalServerError, "The request could not be processed.");
            return;
        }

        context.Response.StatusCode = response.StatusCode;
        foreach (var (name, value) in response.Headers)
            context.Response.Headers[name] = value;
        context.Response.ContentLength = response.Body.Length;
        await context.Response.Body.WriteAsync(response.Body, cancellationToken);
    }

    private static async Task WriteTextAsync(HttpContext context, int statusCode, string text)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(text + "\r\n");
    }
}
