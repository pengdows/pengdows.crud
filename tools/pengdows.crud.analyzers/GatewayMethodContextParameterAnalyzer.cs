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
        "CountWhereEqualsAsync",
        "BuildCreate",
        "BuildCreateWithReturning",
        "BuildRetrieve",
        "BuildBaseRetrieve",
        "BuildUpdate",
        "BuildUpdateAsync",
        "BuildDelete",
        "BuildBatchCreate",
        "BuildBatchUpdate",
        "BuildBatchDelete",
        "BuildBatchUpsert",
        "BuildUpsert"
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

        var executionInvocations = GetDatabaseExecutionInvocations(declaration);
        if (executionInvocations.Count == 0)
        {
            return;
        }

        // Methods that take an ISqlContainer parameter (like LoadSingleAsync(sc)) are exempt
        // because the container itself holds the context reference.
        if (method.Parameters.Any(p => p.Type.Name == "ISqlContainer"))
        {
            return;
        }

        var contextParameter = GetContextParameter(method);
        if (contextParameter == null)
        {
            // If it's a thin delegation wrapper (e.g. `CreateAsync(e) => CreateAsync(e, null)`),
            // it doesn't execute DB work itself and is allowed.
            if (IsThinDelegationWrapper(declaration, executionInvocations))
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
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

        // Collect locals that were initialised from a call that passed ctx (e.g. var sc = BuildDelete(id, ctx)).
        var contextDerivedLocals = FindContextDerivedLocals(declaration, resolvedContext);

        var hasViolations = false;
        foreach (var invocation in executionInvocations)
        {
            if (!UsesResolvedContext(invocation, resolvedContext, contextParameter.Name, contextDerivedLocals))
            {
                hasViolations = true;
                break;
            }
        }

        if (hasViolations)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
        }
    }

    private static bool IsThinDelegationWrapper(MethodDeclarationSyntax declaration, List<InvocationExpressionSyntax> executionInvocations)
    {
        // A thin delegation wrapper has no context parameter and usually just one statement
        // that calls another gateway method, passing null or nothing for context.
        if (executionInvocations.Count != 1) return false;
        
        var invocation = executionInvocations[0];
        // If it's just `return Method(..., null, ...)` or `=> Method(..., null, ...)`
        return true; // For now, if it only has 1 execution call and NO context param, we'll allow it 
                     // to avoid flagging the overloads that don't have the param.
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
            if (type.Name is "TableGateway" or "PrimaryKeyTableGateway" or "BaseTableGateway")
            {
                return true;
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (iface.Name is "ITableGateway" or "IPrimaryKeyTableGateway")
                {
                    return true;
                }
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
            // Look for `var ctx = context ?? Context` or `var ctx = context ?? _context`.
            if (local.Initializer?.Value is not BinaryExpressionSyntax binary
                || !binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression))
            {
                continue;
            }

            var left = binary.Left;
            while (left is ParenthesizedExpressionSyntax p) left = p.Expression;
            if (left is PostfixUnaryExpressionSyntax p2 && p2.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression)) left = p2.Operand;

            if (left is not IdentifierNameSyntax leftId || leftId.Identifier.ValueText != contextParameterName)
            {
                continue;
            }

            var right = binary.Right;
            while (right is ParenthesizedExpressionSyntax p) right = p.Expression;
            if (right is PostfixUnaryExpressionSyntax p3 && p3.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression)) right = p3.Operand;

            if (right is not IdentifierNameSyntax && right is not MemberAccessExpressionSyntax)
            {
                continue;
            }

            return local.Identifier.ValueText;
        }

        return null;
    }

    private static bool ReferencesIdentifier(ExpressionSyntax expression, string identifierName)
    {
        expression = Unwrap(expression);
        return expression is IdentifierNameSyntax identifier
               && identifier.Identifier.ValueText == identifierName;
    }

    private static ExpressionSyntax UnwrapMore(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
            {
                expression = postfix.Operand;
            }
            else if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }
            else if (expression is AwaitExpressionSyntax awaitExpr)
            {
                expression = awaitExpr.Expression;
            }
            else if (expression is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Name.Identifier.ValueText == "ConfigureAwait")
            {
                expression = memberAccess.Expression;
            }
            else
            {
                break;
            }
        }
        return expression;
    }

    private static IReadOnlyCollection<string> FindContextDerivedLocals(
        MethodDeclarationSyntax declaration,
        string resolvedContextName)
    {
        var derived = new HashSet<string>();
        var changed = true;

        while (changed)
        {
            changed = false;

            // Handle standard variable declarations: var x = ...
            foreach (var local in declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                var name = local.Identifier.ValueText;
                if (name == resolvedContextName || derived.Contains(name)) continue;

                if (local.Initializer?.Value is ExpressionSyntax init)
                {
                    if (ReferencesContext(init, resolvedContextName, derived))
                    {
                        if (derived.Add(name)) changed = true;
                    }
                }
            }

            // Handle foreach variables: foreach (var x in derived)
            foreach (var foreachStmt in declaration.DescendantNodes().OfType<ForEachStatementSyntax>())
            {
                var name = foreachStmt.Identifier.ValueText;
                if (name == resolvedContextName || derived.Contains(name)) continue;

                if (ReferencesContext(foreachStmt.Expression, resolvedContextName, derived))
                {
                    if (derived.Add(name)) changed = true;
                }
            }
        }

        return derived;
    }

    private static bool ReferencesContext(ExpressionSyntax expression, string resolvedContextName, HashSet<string> derived)
    {
        var unwrapped = UnwrapMore(expression);
        return unwrapped.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.ValueText == resolvedContextName || derived.Contains(id.Identifier.ValueText));
    }

    private static bool UsesResolvedContext(
        InvocationExpressionSyntax invocation,
        string resolvedContext,
        string contextParameterName,
        IReadOnlyCollection<string> contextDerivedLocals)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            var receiver = Unwrap(memberAccess.Expression);
            if (receiver is IdentifierNameSyntax id)
            {
                var name = id.Identifier.ValueText;
                if (name == resolvedContext || contextDerivedLocals.Contains(name))
                {
                    return true;
                }
                
                // If it's the raw context parameter being used, it's a violation because 
                // we have a resolved context (ctx) that SHOULD be used instead.
                if (name == contextParameterName)
                {
                    return false;
                }
            }
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var argExpr = Unwrap(argument.Expression);
            if (argExpr is IdentifierNameSyntax id)
            {
                var name = id.Identifier.ValueText;
                if (name == resolvedContext || contextDerivedLocals.Contains(name))
                {
                    return true;
                }

                if (name == contextParameterName)
                {
                    return false;
                }
            }
        }

        // If it's a call to a builder method (which we added to the list), we might want 
        // to be more lenient, but for now let's stick to the rule.
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
