using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace pengdows.crud.analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DatabaseContextSingletonAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC001";

    private static readonly ImmutableHashSet<string> BadRegistrationMethods =
    [
        "AddScoped",
        "AddTransient"
    ];

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "pengdows.crud components must be singleton-scoped",
        "Register {0} as a singleton; DatabaseContext and TableGateway types cache state and must not be registered as scoped or transient",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "DatabaseContext owns pool governance, dialect metadata, session normalization, and metrics for a database identity. " +
        "TableGateway and PrimaryKeyTableGateway cache compiled accessors. " +
        "Registering any of them as scoped or transient fragments pool control and breaks load behavior.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, Microsoft.CodeAnalysis.CSharp.SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.ValueText;
        if (!BadRegistrationMethods.Contains(methodName))
        {
            return;
        }

        var matchedTypeName = GetSingletonRequiredTypeName(invocation, context.SemanticModel, context.CancellationToken);
        if (matchedTypeName == null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), matchedTypeName));
    }

    private static string? GetSingletonRequiredTypeName(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name is GenericNameSyntax genericName)
        {
            foreach (var typeArgumentSyntax in genericName.TypeArgumentList.Arguments)
            {
                var typeName = typeArgumentSyntax.ToString();
                if (IsSingletonRequiredType(typeName))
                {
                    return ExtractBaseName(typeName);
                }
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is TypeOfExpressionSyntax typeOfExpression)
            {
                var typeName = typeOfExpression.Type.ToString();
                if (IsSingletonRequiredType(typeName))
                {
                    return ExtractBaseName(typeName);
                }
            }
        }

        return null;
    }

    private static bool IsSingletonRequiredType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var name = ExtractBaseName(typeName!);
        return name is "DatabaseContext" or "IDatabaseContext"
               || name.EndsWith("TableGateway", StringComparison.Ordinal);
    }

    private static string ExtractBaseName(string typeName)
    {
        // Strip generic type arguments: "ITableGateway<Provider, Guid>" → "ITableGateway"
        var angleBracket = typeName.IndexOf('<');
        if (angleBracket >= 0)
        {
            typeName = typeName.Substring(0, angleBracket).TrimEnd();
        }

        // Strip namespace prefix: "pengdows.crud.TableGateway" → "TableGateway"
        var dot = typeName.LastIndexOf('.');
        if (dot >= 0)
        {
            typeName = typeName.Substring(dot + 1);
        }

        return typeName;
    }
}
