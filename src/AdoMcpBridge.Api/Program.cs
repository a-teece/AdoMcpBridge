var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok("ok"));
await app.RunAsync();

// Exposed so WebApplicationFactory<Program> can boot the host from tests.
public partial class Program;
