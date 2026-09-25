using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace pengdows.crud.analyzers;

/// <summary>
/// Prevents consumers from depending on public compatibility leftovers that are not part of
/// the supported application-facing contract.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompatibilityLeakAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "PGC027";

    internal static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Do not use unsupported compatibility surface",
        "Do not use {0}; it is retained only for binary compatibility and is not supported application API",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
        "The referenced symbol is public only for 2.x compatibility. Use the supported pengdows.crud contract instead.");

    private static readonly ImmutableHashSet<string> BlockedTypes =
    [
        "pengdows.crud.EphemeralSecureString",
        "pengdows.crud.IEphemeralSecureString",
        "pengdows.crud.TypeCoercionOptions",
        "pengdows.crud.connection.ConnectionLocalState",
        "pengdows.crud.connection.IConnectionLocalState",
        "pengdows.crud.threading.ILockerAsync",
        "pengdows.crud.tenant.ITenantConfiguration",
        "pengdows.crud.enums.SqlStandardLevel",
        "pengdows.crud.types.attributes.AllowZeroDateAttribute",
        "pengdows.crud.types.attributes.AsStringAttribute",
        "pengdows.crud.types.attributes.CaseFoldOnReadAttribute",
        "pengdows.crud.types.attributes.CaseInsensitiveAttribute",
        "pengdows.crud.types.attributes.ComputedAttribute",
        "pengdows.crud.types.attributes.ConcurrencyTokenAttribute",
        "pengdows.crud.types.attributes.CurrencyAttribute",
        "pengdows.crud.types.attributes.DbEnumAttribute",
        "pengdows.crud.types.attributes.EnumStorage",
        "pengdows.crud.types.attributes.JsonContractAttribute",
        "pengdows.crud.types.attributes.MaxLengthForInlineAttribute",
        "pengdows.crud.types.attributes.RangeTypeAttribute",
        "pengdows.crud.types.attributes.SpatialTypeAttribute"
    ];

    private static readonly ImmutableHashSet<string> BlockedProperties =
    [
        "pengdows.crud.DatabaseContext.DataSource",
        "pengdows.crud.IDatabaseContext.DataSource",
        "pengdows.crud.TransactionContext.DataSource",
        "pengdows.crud.IDataSourceInformation.StandardCompliance",
        "pengdows.crud.dialects.IDatabaseProductInfo.StandardCompliance",
        "pengdows.crud.dialects.ISqlDialect.MaxSupportedStandard"
    ];

    // Fixed at construction: readable, but their setters are public only for 2.0.5 binary
    // compatibility and do nothing (3.0 makes them init-only), so any assignment is a caller bug.
    private static readonly ImmutableHashSet<string> WriteBlockedProperties =
    [
        "pengdows.crud.DatabaseContext.ReadWriteMode",
        "pengdows.crud.DatabaseContext.ProcWrappingStyle"
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        if (string.Equals(context.Compilation.AssemblyName, "pengdows.crud", StringComparison.Ordinal))
        {
            return;
        }

        if (context.Node is not IdentifierNameSyntax identifier)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        var blockedName = GetBlockedName(symbol) ?? GetBlockedWriteName(symbol, identifier);
        if (blockedName == null || IsDeclarationName(identifier, symbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), blockedName));
    }

    private static string? GetBlockedName(ISymbol? symbol)
    {
        if (symbol == null)
        {
            return null;
        }

        if (symbol is IPropertySymbol property
            && BlockedProperties.Contains(GetQualifiedName(property.ContainingType) + "." + property.Name))
        {
            return property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        }

        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        if (type == null)
        {
            return null;
        }

        var qualifiedName = GetQualifiedName(type);
        return BlockedTypes.Contains(qualifiedName) ? type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) : null;
    }

    // Name of a write-blocked property when this identifier is the target of an assignment
    // (context.ReadWriteMode = x, or ReadWriteMode = x in an object initializer); reads return null.
    private static string? GetBlockedWriteName(ISymbol? symbol, IdentifierNameSyntax identifier)
    {
        if (symbol is not IPropertySymbol property
            || !WriteBlockedProperties.Contains(GetQualifiedName(property.ContainingType) + "." + property.Name))
        {
            return null;
        }

        ExpressionSyntax target = identifier;
        if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
        {
            target = memberAccess;
        }

        return target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target
            ? $"the setter of {property.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} (fixed at construction; assigning it has no effect)"
            : null;
    }

    private static string GetQualifiedName(INamedTypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
    }

    private static bool IsDeclarationName(IdentifierNameSyntax identifier, ISymbol? symbol)
    {
        if (symbol == null)
        {
            return false;
        }

        return symbol.DeclaringSyntaxReferences.Any(reference =>
            reference.SyntaxTree == identifier.SyntaxTree
            && reference.Span.Contains(identifier.Span));
    }
}
