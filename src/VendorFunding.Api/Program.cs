using VendorFunding.Api;
using VendorFunding.Core;
using VendorFunding.Data;

var builder = WebApplication.CreateBuilder(args);

// Connection string comes from appsettings.json, user secrets, or the
// ConnectionStrings__VendorFunding environment variable (used in CI).
var connectionString = builder.Configuration.GetConnectionString("VendorFunding")
    ?? throw new InvalidOperationException("Connection string 'VendorFunding' is not configured.");

builder.Services.AddSingleton<IFundingRepository>(_ => new SqlFundingRepository(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapFundingEndpoints();

app.Run();

// Lets the integration tests start the app in memory with WebApplicationFactory<Program>.
public partial class Program { }
