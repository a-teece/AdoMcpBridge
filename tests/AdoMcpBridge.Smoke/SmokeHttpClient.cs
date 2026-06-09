using System.Net.Http.Headers;

namespace AdoMcpBridge.Smoke;

internal static class SmokeHttpClient
{
    public static HttpClient Create(Uri baseAddress)
    {
        var client = new HttpClient
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AdoMcpBridge-Smoke", "1.0"));
        return client;
    }
}
