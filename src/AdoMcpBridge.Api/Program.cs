using AdoMcpBridge.Api.CustomTools;
using AdoMcpBridge.Api.CustomTools.Tools;
using AdoMcpBridge.Api.Endpoints;
using AdoMcpBridge.Api.Middleware;
using AdoMcpBridge.Api.Options;
using AdoMcpBridge.Api.Proxy;
using AdoMcpBridge.Api.Telemetry;
using AdoMcpBridge.Core.Abstractions;
using AdoMcpBridge.Core.BlobStorage;
using AdoMcpBridge.Core.DependencyInjection;
using AdoMcpBridge.Core.Entra;
using AdoMcpBridge.Core.OAuth;
using AdoMcpBridge.Core.Time;
using Azure.Core;
using Azure.Identity;

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
// IAuthorizationSessionCache is registered (SQL-backed) inside
// AddBridgeDataServices; the in-memory implementation remains for tests.
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddRazorPages();

// Blob storage for upload slots (large field write path, ADR-0003).
builder.Services.AddBlobSlotStore(builder.Configuration);

// ADO REST client authenticated via managed identity.
// The MI must be a member of the ADO organisation with work-item edit rights.
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddHttpClient<IAdoRestClient, AdoRestClient>();

// Custom MCP tools — registered as ICustomMcpTool so the middleware can
// resolve them all at once via IEnumerable<ICustomMcpTool>.
builder.Services.AddSingleton<ICustomMcpTool, DownloadFieldTool>();
builder.Services.AddSingleton<ICustomMcpTool, CreateUploadSlotTool>();
builder.Services.AddSingleton<ICustomMcpTool, WriteFieldFromSlotTool>();

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
