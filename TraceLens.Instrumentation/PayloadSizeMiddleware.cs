using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TraceLens.Instrumentation;

/// <summary>
/// İstek ve yanıt gövdesinin byte cinsinden boyutunu aktif server span'ine yazar.
/// "Response şişkinliği" analizleri bu tag'lere dayanır.
/// </summary>
public sealed class PayloadSizeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength is { } requestSize)
            activity.SetTag(TraceLensTags.RequestBodySize, requestSize);

        var originalBody = context.Response.Body;
        var countingBody = new CountingStream(originalBody);
        context.Response.Body = countingBody;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
            activity.SetTag(TraceLensTags.ResponseBodySize, countingBody.BytesWritten);
        }
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            BytesWritten += count;
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

public static class ApplicationBuilderExtensions
{
    /// <summary>Payload boyutu middleware'ini ekler. Pipeline'ın başına yakın çağırın.</summary>
    public static IApplicationBuilder UseTraceLens(this IApplicationBuilder app) =>
        app.UseMiddleware<PayloadSizeMiddleware>();
}
