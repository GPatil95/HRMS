using System.Text.Json.Serialization;
using Asp.Versioning;
using Hangfire;
using Hangfire.PostgreSql;
using Hrms.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Hrms.Api.Hosting;

public static class ApiSetup
{
    public static void AddHrmsApi(this WebApplicationBuilder builder)
    {
        var s = builder.Services;
        var cfg = builder.Configuration;
        s.AddProblemDetails();
        s.AddExceptionHandler<GlobalExceptionHandler>();
        s.AddControllers().AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        });
        s.AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(1, 0);
            o.ReportApiVersions = true;
            o.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddMvc().AddApiExplorer(o =>
        {
            o.GroupNameFormat = "'v'VVV";
            o.SubstituteApiVersionInUrl = true;
        });
        s.AddOpenApi("v1");
        s.AddHrmsAuthentication(builder.Environment);

        var keysPath = cfg["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            s.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        var platformCs = cfg.GetConnectionString("Platform")!;
        var redisCs = cfg.GetConnectionString("Redis");
        var health = s.AddHealthChecks().AddNpgSql(platformCs, name: "platform-db", tags: ["ready"]);
        health.AddCheck<CryptoHealthCheck>("crypto", tags: ["ready"]);
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            health.AddRedis(redisCs, name: "redis", tags: ["ready"]);
        }

        s.AddOpenTelemetry().ConfigureResource(r => r.AddService("hrms-api")).WithTracing(t =>
        {
            t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"));
            t.AddHttpClientInstrumentation();
            t.AddNpgsql();
            if (!string.IsNullOrWhiteSpace(cfg["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            {
                t.AddOtlpExporter();
            }
        });

        if (string.Equals(cfg["Jobs:Mode"], "Hangfire", StringComparison.OrdinalIgnoreCase))
        {
            s.AddHangfire(c => c.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(platformCs), new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
            if (cfg.GetValue("Jobs:RunServer", true))
            {
                s.AddHangfireServer();
            }
        }
    }

    public static void UseHrmsPipeline(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseMiddleware<RequestContextMiddleware>();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<AntiforgeryMiddleware>();
        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "HRMS API v1"));
        }
    }
}
