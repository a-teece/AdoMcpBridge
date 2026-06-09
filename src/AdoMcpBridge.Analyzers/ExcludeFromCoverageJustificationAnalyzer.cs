using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AdoMcpBridge.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ExcludeFromCoverageJustificationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ADOMCP002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "[ExcludeFromCodeCoverage] requires non-empty Justification",
        messageFormat: "[ExcludeFromCodeCoverage] must specify a non-empty Justification argument",
        category: "Coverage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Enforces audit trail when code is excluded from coverage measurement.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Attribute);
    }

    private static void Analyze(SyntaxNodeAnalysisContext ctx)
    {
        var attr = (AttributeSyntax)ctx.Node;
        var typeInfo = ctx.SemanticModel.GetTypeInfo(attr, ctx.CancellationToken);
        var name = typeInfo.Type?.ToDisplayString();
        if (name != "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute")
        {
            return;
        }

        var args = attr.ArgumentList?.Arguments;
        string? justification = null;
        if (args is { } list)
        {
            foreach (var a in list)
            {
                if (a.NameEquals?.Name.Identifier.ValueText == "Justification" &&
                    a.Expression is LiteralExpressionSyntax lit &&
                    lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    justification = lit.Token.ValueText;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(justification))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, attr.GetLocation()));
        }
    }
}
