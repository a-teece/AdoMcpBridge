using AdoMcpBridge.Api.Endpoints;
using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.OAuth;
using AdoMcpBridge.Core.Time;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<AdoMcpOptions>().Bind(builder.Configuration.GetSection("AdoMcp"));
builder.Services.AddSingleton<WrapperTokenMinter>();
builder.Services.AddSingleton<PkceValidator>();
builder.Services.AddSingleton<AuthorizeRequestValidator>();
builder.Services.AddSingleton<IAuthorizationSessionCache, InMemoryAuthorizationSessionCache>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddRazorPages();

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapMetadata();
app.MapRegister();
app.MapConsentSubmit();
app.MapRazorPages();
await app.RunAsync();

// Exposed so WebApplicationFactory<Program> can boot the host from tests.
public partial class Program;
