using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AdoMcpBridge.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoTokenIntoLoggerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ADOMCP001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Do not log tokens, codes, or PKCE verifiers",
        messageFormat: "Symbol '{0}' looks like a secret (token/code/verifier) and must not be passed to ILogger",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents accidental logging of OAuth tokens, authorization codes, or PKCE verifiers.");

    private static readonly string[] ForbiddenSubstrings =
    {
        "accesstoken", "refreshtoken", "idtoken", "bearertoken",
        "authcode", "authorizationcode",
        "codeverifier", "pkceverifier", "pkcecode",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol as IMethodSymbol;
        if (symbol is null) return;

        var containing = symbol.ContainingType?.ToDisplayString() ?? "";
        if (containing != "Microsoft.Extensions.Logging.LoggerExtensions" &&
            containing != "Microsoft.Extensions.Logging.ILogger")
        {
            return;
        }

        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            var name = ExtractName(arg.Expression);
            if (name is null) continue;
            var lower = name.ToLowerInvariant();
            foreach (var bad in ForbiddenSubstrings)
            {
                if (lower.Contains(bad))
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(Rule, arg.Expression.GetLocation(), name));
                    break;
                }
            }
        }
    }

    private static string? ExtractName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        _ => null,
    };
}
