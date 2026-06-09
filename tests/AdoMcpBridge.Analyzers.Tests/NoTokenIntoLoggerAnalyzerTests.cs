using Microsoft.CodeAnalysis.Testing;

namespace AdoMcpBridge.Analyzers.Tests;

public sealed class NoTokenIntoLoggerAnalyzerTests
{
    [Fact]
    public Task Flags_local_named_accessToken_into_LogInformation()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                void M(ILogger logger)
                {
                    var accessToken = "secret";
                    logger.LogInformation("got {Tok}", {|#0:accessToken|});
                }
            }
            """;
        var expected = new DiagnosticResult("ADOMCP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("accessToken");
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Flags_parameter_named_codeVerifier_into_LogDebug()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                void M(ILogger logger, string codeVerifier)
                {
                    logger.LogDebug("v={V}", {|#0:codeVerifier|});
                }
            }
            """;
        var expected = new DiagnosticResult("ADOMCP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("codeVerifier");
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Does_not_flag_unrelated_variable()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                void M(ILogger logger)
                {
                    var userId = "abc";
                    logger.LogInformation("uid={U}", userId);
                }
            }
            """;
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src);
    }

    [Fact]
    public Task Flags_pkce_verifier_property_access()
    {
        const string src = """
            using Microsoft.Extensions.Logging;
            class C
            {
                string PkceVerifier { get; set; } = "";
                void M(ILogger logger)
                {
                    logger.LogInformation("v={V}", {|#0:PkceVerifier|});
                }
            }
            """;
        var expected = new DiagnosticResult("ADOMCP001", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0)
            .WithArguments("PkceVerifier");
        return AnalyzerTestHarness<NoTokenIntoLoggerAnalyzer>.VerifyAsync(src, expected);
    }
}
