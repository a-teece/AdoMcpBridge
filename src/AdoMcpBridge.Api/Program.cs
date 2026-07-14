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

// ADO REST client authenticated with the caller's own delegated ADO token.
// CustomToolMiddleware performs a dedicated OBO/refresh-token swap for the classic
// Azure DevOps REST resource (Entra AdoRestScopes) before invoking a native tool
// and stashes the result on HttpContext.Items; HttpContextAdoAccessTokenProvider
// reads it back so every ADO call is attributed to — and permission-scoped to —
// the real end user, not the bridge's identity. This is a separate token from the
// MCP-server token EntraTokenSwapMiddleware puts on the Authorization header,
// because the classic REST API rejects the MCP-audience token. (The bridge managed
// identity no longer needs ADO-organisation membership for this path; removing that
// grant is a separate infra follow-up.)
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IAdoAccessTokenProvider, HttpContextAdoAccessTokenProvider>();
builder.Services.AddHttpClient<IAdoRestClient, AdoRestClient>();

// Field-type cache used by the slim work-item tools to know which fields
// to stub — populated lazily on the first call per org, then held for the
// process lifetime (field types change only when org admins add custom fields).
builder.Services.AddSingleton<IWorkItemFieldTypeCache, WorkItemFieldTypeCache>();

// Custom MCP tools — registered as ICustomMcpTool so the middleware can
// resolve them all at once via IEnumerable<ICustomMcpTool>.
builder.Services.AddSingleton<ICustomMcpTool, DownloadFieldTool>();
builder.Services.AddSingleton<ICustomMcpTool, CreateUploadSlotTool>();
builder.Services.AddSingleton<ICustomMcpTool, WriteFieldFromSlotTool>();
builder.Services.AddSingleton<ICustomMcpTool, WitGetSlimTool>();
builder.Services.AddSingleton<ICustomMcpTool, WitGetBatchSlimTool>();

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
