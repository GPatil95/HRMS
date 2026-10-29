using Hrms.Api.Hosting;
using Hrms.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHrmsInfrastructure(builder.Configuration);
builder.AddHrmsApi();

var app = builder.Build();

// Fail fast on master-key misconfiguration and log a safe fingerprint so operators can
// compare the API key with the CLI's (same source => identical fingerprint). Never logs key material.
_ = app.Services.GetRequiredService<Hrms.Application.Abstractions.IPlatformMasterKeyProvider>().Key;

app.UseHrmsPipeline();
await app.RunAsync();

public partial class Program;
