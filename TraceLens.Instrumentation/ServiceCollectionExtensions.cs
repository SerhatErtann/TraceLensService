using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TraceLens.Instrumentation;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// OpenTelemetry tracing'i TraceLens kurallarıyla kaydeder. Ayarlar "TraceLens"
    /// config bölümünden okunur, <paramref name="configure"/> ile ezilebilir.
    /// </summary>
    public static IServiceCollection AddTraceLens(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<TraceLensOptions>? configure = null)
    {
        var options = new TraceLensOptions();
        configuration.GetSection(TraceLensOptions.SectionName).Bind(options);
        configure?.Invoke(options);

        var serviceName = options.ServiceName
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? "unknown-service";

        services.AddSingleton(options);
        services.AddSingleton<IJobTracer, JobTracer>();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName)
                .AddAttributes(new Dictionary<string, object>
                {
                    [TraceLensTags.AppType] = options.AppType.ToString().ToLowerInvariant(),
                    ["deployment.environment.name"] = options.Environment
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(TraceLensTracer.SourceName)
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.Filter = ctx => !IsExcluded(ctx.Request.Path, options.ExcludedPaths);
                        o.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(o =>
                    {
                        o.RecordException = true;
                        // Varsayılan ad sadece "GET"; waterfall'da hedef görünsün diye host + path eklenir.
                        o.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            if (request.RequestUri is { } uri)
                                activity.DisplayName = $"{request.Method} {uri.Authority}{uri.AbsolutePath}";
                        };
                    });

                if (options.UseEntityFrameworkCore)
                    tracing.AddEntityFrameworkCoreInstrumentation(o =>
                        o.EnrichWithIDbCommand = (activity, command) => SqlSpanNamer.Apply(activity, command.CommandText));

                if (options.UseSqlClient)
                    tracing.AddSqlClientInstrumentation();

                tracing.AddOtlpExporter(o => o.Endpoint = new Uri(options.OtlpEndpoint));
            });

        return services;
    }

    private static bool IsExcluded(PathString path, string[] excludedPaths) =>
        excludedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
