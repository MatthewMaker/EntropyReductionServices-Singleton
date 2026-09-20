using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EntropyReductionServices.Analyzers
{
    /// <summary>
    /// Enforces the MonoBehaviourSingleton contract, documented in the package's
    /// Documentation~/contract.md. All five rules live in one analyzer so they share a single
    /// compilation-start lookup and a single pass over the relevant syntax kinds.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SingletonAnalyzer : DiagnosticAnalyzer
    {
        // MUST track the runtime type's namespace. GetTypeByMetadataName returns null on a
        // mismatch and Initialize then registers no actions at all, so a stale string here does
        // not fail the analyzer build or any test — it silently disables all five rules.
        // SingletonNamespaceTests pins the other end of this string.
        private const string SingletonBaseMetadataName =
            "EntropyReductionServices.Singletons.MonoBehaviourSingletonBase`1";

        private const string MonoBehaviourMetadataName = "UnityEngine.MonoBehaviour";

        private const string InstancePropertyName = "Instance";
        private const string AvailablePropertyName = "IsAvailable";
        private const string TryGetMethodName = "TryGetInstance";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                SingletonDiagnostics.MissingBaseCall,
                SingletonDiagnostics.CachedInstance,
                SingletonDiagnostics.UnguardedTeardownAccess,
                SingletonDiagnostics.ConstructionTimeAccess,
                SingletonDiagnostics.HidesBaseMessage);

        /// <summary>
        /// Resolves the singleton base type once per compilation and registers nothing at all when
        /// it is absent. Assemblies that do not reference the package therefore pay no per-node
        /// analysis cost, which matters for an analyzer shipped to consumers.
        /// </summary>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(start =>
            {
                var singletonBase = start.Compilation.GetTypeByMetadataName(SingletonBaseMetadataName);
                if (singletonBase == null) return;

                var monoBehaviour = start.Compilation.GetTypeByMetadataName(MonoBehaviourMetadataName);

                start.RegisterSyntaxNodeAction(
                    ctx => AnalyzeMethodDeclaration(ctx, singletonBase),
                    SyntaxKind.MethodDeclaration);

                start.RegisterSyntaxNodeAction(
                    ctx => AnalyzeInstanceAccess(ctx, singletonBase, monoBehaviour),
                    SyntaxKind.SimpleMemberAccessExpression);
            });
        }

        // -----------------------------------------------------------------------------------
        // ERS0001 / ERS0005 — Awake and OnDestroy declarations on singleton subclasses
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Checks Awake and OnDestroy declarations inside singleton subclasses for the two ways of
        /// accidentally severing the base implementation: an override that never chains, and a
        /// declaration that hides the virtual instead of overriding it.
        /// </summary>
        private static void AnalyzeMethodDeclaration(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol singletonBase)
        {
            var declaration = (MethodDeclarationSyntax)context.Node;
            var name = declaration.Identifier.ValueText;
            if (name != "Awake" && name != "OnDestroy") return;

            if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken)
                    is IMethodSymbol method))
                return;

            var owner = method.ContainingType;
            if (owner == null || !DerivesFrom(owner, singletonBase)) return;

            if (method.IsOverride)
            {
                // Only our own virtuals matter. An override of something else that happens to be
                // called Awake is not this analyzer's business.
                var overridden = method.OverriddenMethod;
                if (overridden == null || !DerivesFrom(overridden.ContainingType, singletonBase)) return;
                if (CallsBase(declaration, name)) return;

                context.ReportDiagnostic(Diagnostic.Create(
                    SingletonDiagnostics.MissingBaseCall,
                    declaration.Identifier.GetLocation(),
                    owner.Name,
                    name));
                return;
            }

            // Not an override: does a base singleton type declare this virtual anyway?
            if (HasBaseVirtual(owner.BaseType, name, singletonBase))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SingletonDiagnostics.HidesBaseMessage,
                    declaration.Identifier.GetLocation(),
                    owner.Name,
                    name));
            }
        }

        /// <summary>
        /// True when the declaration contains a base.&lt;name&gt;(...) invocation anywhere,
        /// including in an expression body or inside a conditional branch. Deliberately
        /// flow-insensitive: proving the call always executes is not worth the false negatives
        /// that a stricter check would trade for.
        /// </summary>
        private static bool CallsBase(MethodDeclarationSyntax declaration, string name)
        {
            foreach (var node in declaration.DescendantNodes())
            {
                if (!(node is InvocationExpressionSyntax invocation)) continue;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access)) continue;
                if (!(access.Expression is BaseExpressionSyntax)) continue;
                if (access.Name.Identifier.ValueText == name) return true;
            }

            return false;
        }

        /// <summary>
        /// Walks up from the given type looking for a virtual or override method of this name
        /// declared by a singleton base class.
        /// </summary>
        private static bool HasBaseVirtual(INamedTypeSymbol type, string name, INamedTypeSymbol singletonBase)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (!DerivesFrom(current, singletonBase)) continue;

                foreach (var member in current.GetMembers(name))
                {
                    if (member is IMethodSymbol m && (m.IsVirtual || m.IsOverride)) return true;
                }
            }

            return false;
        }

        // -----------------------------------------------------------------------------------
        // ERS0002 / ERS0003 / ERS0004 — reads of a singleton Instance property
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Classifies every read of a singleton's Instance property by the context it appears in.
        /// The cases are mutually exclusive and checked most-severe first: construction-time
        /// access throws, field caching goes stale, teardown access may be null.
        /// </summary>
        private static void AnalyzeInstanceAccess(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol singletonBase,
            INamedTypeSymbol monoBehaviour)
        {
            var access = (MemberAccessExpressionSyntax)context.Node;
            if (access.Name.Identifier.ValueText != InstancePropertyName) return;

            var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
            if (!(symbol is IPropertySymbol property)) return;
            if (property.ContainingType == null) return;
            if (!DerivesFrom(property.ContainingType, singletonBase)) return;

            // Report the type the author actually wrote ("AudioBus"), not the generic that
            // declares the property ("MonoBehaviourSingleton").
            var receiver = context.SemanticModel
                .GetSymbolInfo(access.Expression, context.CancellationToken).Symbol as INamedTypeSymbol;
            var singletonName = receiver?.Name ?? property.ContainingType.Name;

            var enclosing = FindEnclosingMember(access);

            if (TryReportConstructionTime(context, access, enclosing, monoBehaviour, singletonName)) return;
            if (TryReportFieldCache(context, access, singletonName)) return;
            TryReportTeardown(context, access, enclosing, singletonName);
        }

        /// <summary>
        /// ERS0004: instance field initializers and constructors on a MonoBehaviour run during
        /// deserialization, where Unity refuses object lookup and creation outright.
        /// </summary>
        private static bool TryReportConstructionTime(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            SyntaxNode enclosing,
            INamedTypeSymbol monoBehaviour,
            string singletonName)
        {
            if (monoBehaviour == null) return false;

            var owner = context.SemanticModel.GetEnclosingSymbol(access.SpanStart, context.CancellationToken)
                ?.ContainingType;
            if (owner == null || !DerivesFrom(owner, monoBehaviour)) return false;

            string where = null;

            if (enclosing is ConstructorDeclarationSyntax ctor && !ctor.Modifiers.Any(SyntaxKind.StaticKeyword))
                where = "a constructor";
            else if (enclosing is FieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.StaticKeyword))
                where = "an instance field initializer";

            if (where == null) return false;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.ConstructionTimeAccess,
                access.GetLocation(),
                owner.Name,
                singletonName,
                where));
            return true;
        }

        /// <summary>
        /// ERS0002: the read feeds a field, either through an initializer or an assignment.
        /// Locals are intentionally not flagged — a value used within one method call is exactly
        /// the intended usage.
        /// </summary>
        private static bool TryReportFieldCache(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            string singletonName)
        {
            var target = FindAssignedField(context, access);
            if (target == null) return false;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.CachedInstance,
                access.GetLocation(),
                target.Name,
                singletonName));
            return true;
        }

        /// <summary>
        /// Resolves the field this expression is ultimately stored into, whether by field
        /// initializer or by assignment, and null when it is not stored in a field at all.
        /// </summary>
        private static IFieldSymbol FindAssignedField(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access)
        {
            for (SyntaxNode node = access; node != null; node = node.Parent)
            {
                // Stop at anything that establishes a new value context; beyond these the
                // expression is no longer "the thing being stored".
                if (node is StatementSyntax && !(node is ExpressionStatementSyntax)) break;
                if (node is LambdaExpressionSyntax || node is AnonymousMethodExpressionSyntax) break;
                if (node is ArgumentSyntax) break;

                if (node is AssignmentExpressionSyntax assignment && assignment.Right.Contains(access))
                {
                    return context.SemanticModel
                        .GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol as IFieldSymbol;
                }

                if (node is VariableDeclaratorSyntax declarator &&
                    declarator.Parent?.Parent is FieldDeclarationSyntax)
                {
                    return context.SemanticModel
                        .GetDeclaredSymbol(declarator, context.CancellationToken) as IFieldSymbol;
                }
            }

            return null;
        }

        // OnDisable is deliberately absent. It runs both during teardown and during ordinary
        // play (SetActive(false), a disabled component, a pooled object returning to its pool),
        // and nothing in the syntax distinguishes the two. Flagging it warns on the common,
        // correct case, so it is left to the author.
        private static readonly string[] TeardownMethods = { "OnDestroy", "OnApplicationQuit" };

        /// <summary>
        /// ERS0003: a dereference of Instance inside a teardown callback. Only direct dereferences
        /// are reported — passing the value along, comparing it, or using '?.' is already safe —
        /// and the whole method is exempted when it mentions IsAvailable or TryGetInstance, which
        /// is a deliberately crude but predictable way to honour an explicit guard.
        /// </summary>
        private static void TryReportTeardown(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            SyntaxNode enclosing,
            string singletonName)
        {
            if (!(enclosing is MethodDeclarationSyntax method)) return;

            var methodName = method.Identifier.ValueText;
            var isTeardown = false;
            foreach (var candidate in TeardownMethods)
            {
                if (methodName != candidate) continue;
                isTeardown = true;
                break;
            }

            if (!isTeardown) return;
            if (!IsDereferenced(access)) return;
            if (HasExplicitGuard(method)) return;

            var owner = context.SemanticModel
                .GetEnclosingSymbol(access.SpanStart, context.CancellationToken)?.ContainingType;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.UnguardedTeardownAccess,
                access.GetLocation(),
                owner?.Name ?? "<unknown>",
                singletonName,
                methodName));
        }

        /// <summary>
        /// True for 'Foo.Instance.Bar' and 'Foo.Instance?.Bar', false for bare reads.
        ///
        /// '?.' used to be exempt, on the reasoning that it collapses to a no-op when Instance is
        /// null. That stopped being true when Instance began returning the destroyed component
        /// during teardown rather than null: '?.' tests the reference, not Unity's == overload, so
        /// it sees a live C# object and proceeds. It is now the most dangerous of the three
        /// spellings, because it reads as a guard and is not one. IsAvailable and TryGetInstance
        /// both consult the overload and still work.
        /// </summary>
        private static bool IsDereferenced(MemberAccessExpressionSyntax access)
        {
            if (access.Parent is ConditionalAccessExpressionSyntax conditional &&
                conditional.Expression == access)
                return true;

            return access.Parent is MemberAccessExpressionSyntax parent && parent.Expression == access;
        }

        /// <summary>True when the method mentions IsAvailable or TryGetInstance anywhere.</summary>
        private static bool HasExplicitGuard(MethodDeclarationSyntax method)
        {
            foreach (var node in method.DescendantNodes())
            {
                if (!(node is IdentifierNameSyntax identifier)) continue;
                var text = identifier.Identifier.ValueText;
                if (text == AvailablePropertyName || text == TryGetMethodName) return true;
            }

            return false;
        }

        // -----------------------------------------------------------------------------------
        // Shared helpers
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Finds the member declaration an expression sits in — a method, constructor, property,
        /// or field declaration — so callers can reason about the execution context.
        /// </summary>
        private static SyntaxNode FindEnclosingMember(SyntaxNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current is MemberDeclarationSyntax member) return member;
            }

            return null;
        }

        /// <summary>
        /// True when the type or any of its bases is a construction of the given open generic
        /// type. Comparison is against OriginalDefinition, so MonoBehaviourSingletonBase&lt;Foo&gt;
        /// matches the unbound MonoBehaviourSingletonBase&lt;&gt;.
        /// </summary>
        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType)) return true;
            }

            return false;
        }
    }
}
