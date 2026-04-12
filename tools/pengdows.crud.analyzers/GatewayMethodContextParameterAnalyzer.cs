using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace pengdows.crud.analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GatewayMethodContextParameterAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC025";

    private static readonly ImmutableHashSet<string> ExecutionMethodNames =
    [
        "BeginTransaction",
        "BeginTransactionAsync",
        "RetrieveOneAsync",
        "RetrieveAsync",
        "RetrieveStreamAsync",
        "CreateAsync",
        "UpdateAsync",
        "DeleteAsync",
        "UpsertAsync",
        "BatchCreateAsync",
        "BatchUpdateAsync",
        "BatchUpsertAsync",
        "BatchDeleteAsync",
        "LoadSingleAsync",
        "LoadListAsync",
        "LoadStreamAsync",
        "ExecuteNonQueryAsync",
        "ExecuteScalarRequiredAsync",
        "ExecuteScalarOrNullAsync",
        "TryExecuteScalarAsync",
        "ExecuteReaderAsync",
        "CountAllAsync",
        "CountWhereAsync",
        "CountWhereNullAsync",
        "CountWhereEqualsAsync"
    ];

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Gateway methods should accept an execution context",
        "Gateway methods that execute database work must resolve and use ctx = contextArg ?? Context for transactions and multitenancy",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "Public and protected gateway methods that execute database work must accept an IDatabaseContext " +
        "or ITransactionContext parameter, resolve a local execution context via contextArg ?? Context, and route all execution through that resolved context.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, Microsoft.CodeAnalysis.CSharp.SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not MethodDeclarationSyntax declaration)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method)
        {
            return;
        }

        if (method.MethodKind != MethodKind.Ordinary
            || method.IsStatic
            || method.IsOverride
            || method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
        {
            return;
        }

        if (!IsGatewayType(method.ContainingType))
        {
            return;
        }

        var contextParameter = GetContextParameter(method);
        if (contextParameter == null)
        {
            if (GetDatabaseExecutionInvocations(declaration).Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
            }

            return;
        }

        var executionInvocations = GetDatabaseExecutionInvocations(declaration);
        if (executionInvocations.Count == 0)
        {
            return;
        }

        var resolvedContext = FindResolvedContextLocal(declaration, contextParameter.Name);
        if (resolvedContext == null)
        {
            // Allow thin delegation wrappers: all execution calls pass the context parameter
            // directly as an argument (e.g. `=> BatchCreateAsync(entities, context, ct)`).
            if (AllExecutionCallsPassContextDirect(executionInvocations, contextParameter.Name))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
            return;
        }

        // Collect locals that were initialised from a call that passed ctx (e.g. var sc = BuildBaseRetrieve("a", ctx)).
        // LoadListAsync(sc) / LoadSingleAsync(sc) passing such a derived local is considered compliant.
        var contextDerivedLocals = FindContextDerivedLocals(declaration, resolvedContext);

        foreach (var invocation in executionInvocations)
        {
            if (!UsesResolvedContext(invocation, resolvedContext, contextDerivedLocals))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
                return;
            }
        }
    }

    private static IParameterSymbol? GetContextParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (IsContextType(parameter.Type))
            {
                return parameter;
            }
        }

        return null;
    }

    private static List<InvocationExpressionSyntax> GetDatabaseExecutionInvocations(MethodDeclarationSyntax declaration)
    {
        var result = new List<InvocationExpressionSyntax>();

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetInvokedMethodName(invocation);
            if (methodName != null && ExecutionMethodNames.Contains(methodName))
            {
                result.Add(invocation);
            }
        }

        return result;
    }

    private static bool IsGatewayType(INamedTypeSymbol? type)
    {
        while (type != null)
        {
            if (type.Name is "TableGateway" or "PrimaryKeyTableGateway")
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
    }

    private static bool IsContextType(ITypeSymbol type)
    {
        if (type.Name is "IDatabaseContext" or "ITransactionContext")
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name is "IDatabaseContext" or "ITransactionContext")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when every execution-method invocation directly passes the context
    /// parameter as an argument — the thin-delegation-wrapper pattern:
    /// <c>public ValueTask&lt;int&gt; CreateAsync(entities, context, ct) =&gt; BatchCreateAsync(entities, context, ct);</c>
    /// </summary>
    private static bool AllExecutionCallsPassContextDirect(
        List<InvocationExpressionSyntax> executionInvocations,
        string contextParameterName)
    {
        if (executionInvocations.Count == 0)
        {
            return false;
        }

        foreach (var invocation in executionInvocations)
        {
            var passesContext = invocation.ArgumentList.Arguments
                .Any(arg => ReferencesIdentifier(arg.Expression, contextParameterName));

            if (!passesContext)
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindResolvedContextLocal(
        MethodDeclarationSyntax declaration,
        string contextParameterName)
    {
        foreach (var local in declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (local.Initializer?.Value is not BinaryExpressionSyntax binary
                || !binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression))
            {
                continue;
            }

            if (!ReferencesIdentifier(binary.Left, contextParameterName))
            {
                continue;
            }

            // Accept any identifier or member access on the right side — both
            // `context ?? Context` (property) and `context ?? _context` (field) are valid.
            var rhs = Unwrap(binary.Right);
            var rhsIsContextLike = rhs is IdentifierNameSyntax || rhs is MemberAccessExpressionSyntax;
            if (!rhsIsContextLike)
            {
                continue;
            }

            if (local.Identifier.ValueText.Length > 0)
            {
                return local.Identifier.ValueText;
            }
        }

        return null;
    }

    private static bool ReferencesIdentifier(ExpressionSyntax expression, string identifierName)
    {
        expression = Unwrap(expression);
        return expression is IdentifierNameSyntax identifier
               && identifier.Identifier.ValueText == identifierName;
    }

    private static IReadOnlyCollection<string> FindContextDerivedLocals(
        MethodDeclarationSyntax declaration,
        string resolvedContextName)
    {
        var derived = new List<string>();
        foreach (var local in declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (local.Initializer?.Value is not InvocationExpressionSyntax init)
            {
                continue;
            }

            // Case 1: ctx is passed as an argument — e.g. var sc = BuildDelete(id, ctx)
            foreach (var arg in init.ArgumentList.Arguments)
            {
                if (ReferencesIdentifier(arg.Expression, resolvedContextName))
                {
                    derived.Add(local.Identifier.ValueText);
                    goto nextLocal;
                }
            }

            // Case 2: ctx is the receiver — e.g. var sc = ctx.CreateSqlContainer(query)
            if (init.Expression is MemberAccessExpressionSyntax receiverAccess
                && ReferencesIdentifier(receiverAccess.Expression, resolvedContextName))
            {
                derived.Add(local.Identifier.ValueText);
            }

        nextLocal:;
        }

        return derived;
    }

    private static bool UsesResolvedContext(
        InvocationExpressionSyntax invocation,
        string resolvedContext,
        IReadOnlyCollection<string> contextDerivedLocals)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (ReferencesIdentifier(memberAccess.Expression, resolvedContext))
            {
                return true;
            }

            // Accept `derivedLocal.ExecuteNonQueryAsync(...)` where derivedLocal was
            // initialised from a call that received ctx (e.g. var sc = BuildDelete(id, ctx)).
            if (memberAccess.Expression is IdentifierNameSyntax receiverId
                && contextDerivedLocals.Contains(receiverId.Identifier.ValueText))
            {
                return true;
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (ReferencesIdentifier(argument.Expression, resolvedContext))
            {
                return true;
            }

            // Also accept locals derived from ctx (e.g. var sc = BuildBaseRetrieve("a", ctx))
            if (argument.Expression is IdentifierNameSyntax id
                && contextDerivedLocals.Contains(id.Identifier.ValueText))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is PostfixUnaryExpressionSyntax postfix
               && postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
        {
            expression = postfix.Operand;
        }

        return expression;
    }
}
