using Hrms.Api.Hosting;
using Hrms.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHrmsInfrastructure(builder.Configuration);
builder.AddHrmsApi();

var app = builder.Build();
app.UseHrmsPipeline();
await app.RunAsync();

public partial class Program;
