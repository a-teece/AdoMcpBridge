using AdoMcpBridge.Api.Endpoints;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Api.Telemetry;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.DependencyInjection;
using AdoMcpBridge.Core.Entra;
using AdoMcpBridge.Core.OAuth;
using AdoMcpBridge.Core.Time;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBridgeTelemetry(builder.Configuration);
builder.Services.AddOptions<AdoMcpOptions>().Bind(builder.Configuration.GetSection("AdoMcp"));
builder.Services.AddBridgeDataServices(builder.Configuration);
builder.Services.AddEntraClient(builder.Configuration);
builder.Services.AddSingleton<WrapperTokenMinter>();
builder.Services.AddSingleton<PkceValidator>();
// Scoped, not singleton: it consumes ITokenStore, which is scoped to
// the request (EF DbContext lifetime).
builder.Services.AddScoped<AuthorizeRequestValidator>();
builder.Services.AddSingleton<IAuthorizationSessionCache, InMemoryAuthorizationSessionCache>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddRazorPages();
builder.Services.AddMcpProxy(builder.Configuration);

var app = builder.Build();
app.UseBridgeErrorHandling();
app.UseMcpProxy();
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapConnectorInfo();
app.MapMetadata();
app.MapRegister();
app.MapConsentSubmit();
app.MapEntraCallback();
app.MapToken();
app.MapRevoke();
app.MapRazorPages();
await app.RunAsync();

// Exposed so WebApplicationFactory<Program> can boot the host from tests.
public partial class Program;
