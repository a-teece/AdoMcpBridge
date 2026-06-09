using Microsoft.CodeAnalysis.Testing;

namespace AdoMcpBridge.Analyzers.Tests;

public sealed class ExcludeFromCoverageJustificationAnalyzerTests
{
    [Fact]
    public Task Flags_attribute_without_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [{|#0:ExcludeFromCodeCoverage|}]
            class C { }
            """;
        var expected = new DiagnosticResult("ADOMCP002", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0);
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Flags_attribute_with_empty_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [{|#0:ExcludeFromCodeCoverage(Justification = "")|}]
            class C { }
            """;
        var expected = new DiagnosticResult("ADOMCP002", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0);
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Flags_attribute_with_whitespace_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [{|#0:ExcludeFromCodeCoverage(Justification = "   ")|}]
            class C { }
            """;
        var expected = new DiagnosticResult("ADOMCP002", Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .WithLocation(0);
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src, expected);
    }

    [Fact]
    public Task Does_not_flag_attribute_with_real_Justification()
    {
        const string src = """
            using System.Diagnostics.CodeAnalysis;
            [ExcludeFromCodeCoverage(Justification = "Program entry point exercised by integration tests only.")]
            class C { }
            """;
        return AnalyzerTestHarness<ExcludeFromCoverageJustificationAnalyzer>.VerifyAsync(src);
    }
}
