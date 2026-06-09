using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace AdoMcpBridge.Analyzers.Tests;

internal static class AnalyzerTestHarness<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
{
    public sealed class Test : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    {
        public Test()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location));
            TestState.AdditionalReferences.Add(
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Logging.LoggerExtensions).Assembly.Location));
        }
    }

    public static Task VerifyAsync(string source, params DiagnosticResult[] expected)
    {
        var t = new Test { TestCode = source };
        t.ExpectedDiagnostics.AddRange(expected);
        return t.RunAsync();
    }
}
