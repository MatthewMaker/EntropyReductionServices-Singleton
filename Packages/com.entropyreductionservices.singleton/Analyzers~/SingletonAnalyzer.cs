using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EntropyReductionServices.Analyzers
{
    /// <summary>
    /// Enforces the MonoBehaviourSingleton contract, documented in the package's
    /// Documentation~/contract.md. Every rule lives in one analyzer so they share a single
    /// compilation-start lookup and a single pass over the relevant syntax kinds.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SingletonAnalyzer : DiagnosticAnalyzer
    {
        // MUST track the runtime type's namespace. GetTypeByMetadataName returns null on a
        // mismatch and Initialize then registers no actions at all, so a stale string here does
        // not fail the analyzer build or any test — it silently disables every rule.
        // SingletonNamespaceTests pins the other end of this string.
        private const string SingletonBaseMetadataName =
            "EntropyReductionServices.Singletons.MonoBehaviourSingleton`1";

        // A singleton marks itself DontDestroyOnLoad unless this attribute gives it a Scene lifetime.
        private const string LifetimeAttributeMetadataName =
            "EntropyReductionServices.Singletons.SingletonLifetimeAttribute";
        private const string SceneLifetimeMemberName = "Scene";

        private const string MonoBehaviourMetadataName = "UnityEngine.MonoBehaviour";
        private const string ComponentMetadataName = "UnityEngine.Component";
        private const string UnityObjectMetadataName = "UnityEngine.Object";
        private const string SerializationReceiverMetadataName = "UnityEngine.ISerializationCallbackReceiver";
        private const string SerializeFieldMetadataName = "UnityEngine.SerializeField";
        private const string NonSerializedMetadataName = "System.NonSerializedAttribute";

        private const string InstancePropertyName = "Instance";
        private const string HideFlagsName = "hideFlags";
        private const string GameObjectName = "gameObject";
        private const string AvailablePropertyName = "IsAvailable";
        private const string TryGetMethodName = "TryGetInstance";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                SingletonDiagnostics.MissingBaseCall,
                SingletonDiagnostics.CachedInstance,
                SingletonDiagnostics.UnguardedTeardownAccess,
                SingletonDiagnostics.ConstructionTimeAccess,
                SingletonDiagnostics.HidesBaseMessage,
                SingletonDiagnostics.RedundantNullConditional,
                SingletonDiagnostics.BaseCallOutOfOrder,
                SingletonDiagnostics.SerializationCallbackAccess,
                SingletonDiagnostics.HideFlagsAssignment);

        /// <summary>
        /// The Unity and singleton types the rules test against, resolved once per compilation.
        /// Every one except SingletonBase may be null when the compilation lacks it; a rule that
        /// needs a missing one declines to report rather than guess.
        /// </summary>
        private sealed class KnownTypes
        {
            public INamedTypeSymbol SingletonBase;
            public INamedTypeSymbol LifetimeAttribute;
            public INamedTypeSymbol MonoBehaviour;
            public INamedTypeSymbol Component;
            public INamedTypeSymbol UnityObject;
            public INamedTypeSymbol SerializationReceiver;
            public INamedTypeSymbol SerializeField;
            public INamedTypeSymbol NonSerialized;
        }

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

                var compilation = start.Compilation;
                var known = new KnownTypes
                {
                    SingletonBase = singletonBase,
                    LifetimeAttribute = compilation.GetTypeByMetadataName(LifetimeAttributeMetadataName),
                    MonoBehaviour = compilation.GetTypeByMetadataName(MonoBehaviourMetadataName),
                    Component = compilation.GetTypeByMetadataName(ComponentMetadataName),
                    UnityObject = compilation.GetTypeByMetadataName(UnityObjectMetadataName),
                    SerializationReceiver = compilation.GetTypeByMetadataName(SerializationReceiverMetadataName),
                    SerializeField = compilation.GetTypeByMetadataName(SerializeFieldMetadataName),
                    NonSerialized = compilation.GetTypeByMetadataName(NonSerializedMetadataName),
                };

                start.RegisterSyntaxNodeAction(
                    ctx => AnalyzeMethodDeclaration(ctx, singletonBase),
                    SyntaxKind.MethodDeclaration);

                start.RegisterSyntaxNodeAction(
                    ctx => AnalyzeInstanceAccess(ctx, known),
                    SyntaxKind.SimpleMemberAccessExpression);

                // Compound forms too: 'hideFlags |= HideFlags.DontSave' is the usual spelling.
                start.RegisterSyntaxNodeAction(
                    ctx => AnalyzeHideFlagsAssignment(ctx, singletonBase),
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxKind.OrAssignmentExpression,
                    SyntaxKind.AndAssignmentExpression,
                    SyntaxKind.ExclusiveOrAssignmentExpression);
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
                if (CallsBase(declaration, name))
                {
                    ReportBaseCallOutOfOrder(context, declaration, name, owner.Name);
                    return;
                }

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
        /// ERS0007: the base call exists — ERS0001 already let this through — but sits in the
        /// wrong place. Awake's belongs first, OnDestroy's last.
        ///
        /// Only a base call that is a whole statement directly in the method body is considered.
        /// One nested in an if, a loop or a local function is left alone: its position is not a
        /// simple ordering question and CallsBase deliberately accepts it, so guessing here would
        /// turn a permissive rule into a confusing one.
        /// </summary>
        private static void ReportBaseCallOutOfOrder(
            SyntaxNodeAnalysisContext context,
            MethodDeclarationSyntax declaration,
            string name,
            string ownerName)
        {
            var body = declaration.Body;
            if (body == null) return;                       // expression-bodied: first and last

            var statements = body.Statements;
            if (statements.Count < 2) return;               // nothing to be out of order against

            var index = -1;
            for (var i = 0; i < statements.Count; i++)
            {
                if (!IsBaseCallStatement(statements[i], name)) continue;
                index = i;
                break;
            }

            if (index < 0) return;                          // nested somewhere; not our business

            var wantsFirst = name == "Awake";
            var correct = wantsFirst ? index == 0 : index == statements.Count - 1;
            if (correct) return;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.BaseCallOutOfOrder,
                statements[index].GetLocation(),
                ownerName,
                name,
                wantsFirst ? "first, before any other statement" : "last, after any other statement"));
        }

        /// <summary>True when the node is a base.&lt;name&gt;(...) invocation.</summary>
        private static bool IsBaseInvocation(SyntaxNode node, string name) =>
            node is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax access &&
            access.Expression is BaseExpressionSyntax &&
            access.Name.Identifier.ValueText == name;

        /// <summary>True when the statement is exactly 'base.&lt;name&gt;();' and nothing else.</summary>
        private static bool IsBaseCallStatement(StatementSyntax statement, string name) =>
            statement is ExpressionStatementSyntax expression &&
            IsBaseInvocation(expression.Expression, name);

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
                if (IsBaseInvocation(node, name)) return true;
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
        // ERS0002 / ERS0003 / ERS0004 / ERS0006 / ERS0008 — reads of a singleton Instance
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Classifies every read of a singleton's Instance property by the context it appears in.
        /// The cases are mutually exclusive and checked most-severe first: construction-time and
        /// serialization-time access throw, field caching goes stale, teardown access may be null.
        /// </summary>
        private static void AnalyzeInstanceAccess(SyntaxNodeAnalysisContext context, KnownTypes known)
        {
            var access = (MemberAccessExpressionSyntax)context.Node;
            if (access.Name.Identifier.ValueText != InstancePropertyName) return;

            var symbol = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
            if (!(symbol is IPropertySymbol property)) return;
            if (property.ContainingType == null) return;
            if (!DerivesFrom(property.ContainingType, known.SingletonBase)) return;

            // Report the type the author actually wrote ("AudioBus"), not the generic that
            // declares the property ("MonoBehaviourSingleton").
            var receiver = context.SemanticModel
                .GetSymbolInfo(access.Expression, context.CancellationToken).Symbol as INamedTypeSymbol;
            var singletonName = receiver?.Name ?? property.ContainingType.Name;

            var enclosing = FindEnclosingMember(access);

            if (TryReportConstructionTime(context, access, enclosing, known.MonoBehaviour, singletonName)) return;
            if (TryReportSerializationCallback(context, access, known, singletonName)) return;
            if (TryReportFieldCache(context, access, receiver, known, singletonName)) return;
            if (TryReportTeardown(context, access, enclosing, singletonName)) return;
            TryReportRedundantNullConditional(context, access, singletonName);
        }

        /// <summary>
        /// ERS0006: '?.' on a singleton's Instance, outside a teardown callback.
        ///
        /// Runs last and only when TryReportTeardown declined, so a '?.' inside OnDestroy is
        /// reported once, as ERS0003 — the more serious reading, since there the operator looks
        /// like protection and provides none. Outside teardown the same expression is merely dead,
        /// which is what this reports.
        /// </summary>
        private static void TryReportRedundantNullConditional(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            string singletonName)
        {
            if (!IsNullConditionalReceiver(access, out _)) return;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.RedundantNullConditional,
                access.GetLocation(),
                singletonName));
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

            // Syntax first: almost every Instance access sits in an ordinary method, and this
            // rejects those without binding a symbol.
            string where = null;

            if (enclosing is ConstructorDeclarationSyntax ctor && !ctor.Modifiers.Any(SyntaxKind.StaticKeyword))
                where = "a constructor";
            else if (enclosing is FieldDeclarationSyntax field && !field.Modifiers.Any(SyntaxKind.StaticKeyword))
                where = "an instance field initializer";

            if (where == null) return false;

            var owner = context.SemanticModel.GetEnclosingSymbol(access.SpanStart, context.CancellationToken)
                ?.ContainingType;
            if (owner == null || !DerivesFrom(owner, monoBehaviour)) return false;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.ConstructionTimeAccess,
                access.GetLocation(),
                owner.Name,
                singletonName,
                where));
            return true;
        }

        private const string OnValidateName = "OnValidate";
        private const string OnBeforeSerializeName = "OnBeforeSerialize";
        private const string OnAfterDeserializeName = "OnAfterDeserialize";

        /// <summary>
        /// ERS0008: a singleton's Instance read directly in OnValidate on a UnityEngine.Object,
        /// or in OnBeforeSerialize / OnAfterDeserialize on an ISerializationCallbackReceiver.
        ///
        /// A read inside a lambda, anonymous method or local function is not reported: deferring
        /// the work that way —
        /// EditorApplication.delayCall inside OnValidate — is the fix this rule recommends, and
        /// the syntax cannot tell a deferred delegate from one invoked on the spot.
        /// </summary>
        private static bool TryReportSerializationCallback(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            KnownTypes known,
            string singletonName)
        {
            var method = FindEnclosingMethodOutsideDelegates(access);
            if (method == null) return false;

            var name = method.Identifier.ValueText;
            if (name != OnValidateName && name != OnBeforeSerializeName && name != OnAfterDeserializeName)
                return false;

            var owner = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken)?.ContainingType;
            if (owner == null) return false;

            var applies = name == OnValidateName
                ? known.UnityObject != null && DerivesFrom(owner, known.UnityObject)
                : known.SerializationReceiver != null &&
                  owner.AllInterfaces.Contains(known.SerializationReceiver, SymbolEqualityComparer.Default);
            if (!applies) return false;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.SerializationCallbackAccess,
                access.GetLocation(),
                owner.Name,
                name,
                singletonName));
            return true;
        }

        /// <summary>
        /// The method declaration an expression sits directly in, or null when a lambda, anonymous
        /// method or local function intervenes — code in those may run later, elsewhere.
        /// </summary>
        private static MethodDeclarationSyntax FindEnclosingMethodOutsideDelegates(SyntaxNode node)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                if (current is AnonymousFunctionExpressionSyntax || current is LocalFunctionStatementSyntax)
                    return null;
                if (current is MethodDeclarationSyntax method) return method;
                if (current is MemberDeclarationSyntax) return null;
            }

            return null;
        }

        /// <summary>
        /// ERS0002: the read feeds a field, either through an initializer or an assignment, and
        /// that field can outlive the singleton it holds. Locals are intentionally not flagged — a
        /// value used within one method call is exactly the intended usage.
        /// </summary>
        private static bool TryReportFieldCache(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            INamedTypeSymbol receiver,
            KnownTypes known,
            string singletonName)
        {
            var target = FindAssignedField(context, access);
            if (target == null) return false;

            var reason = WhyFieldCanOutlive(target, receiver, known, singletonName);
            if (reason == null) return false;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.CachedInstance,
                access.GetLocation(),
                target.Name,
                singletonName,
                reason));
            return true;
        }

        /// <summary>
        /// Why a field holding this singleton can end up pointing at a dead one, or null when it
        /// cannot. The one exempt shape is a private, non-serialized instance field on a
        /// Component, holding a singleton with the default Application lifetime: the holder dies with its scene or at quit,
        /// and the singleton outlives both. Any doubt — an unresolved type, a missing Unity symbol
        /// — reports, which is the behaviour this rule had before it was narrowed.
        /// </summary>
        private static string WhyFieldCanOutlive(
            IFieldSymbol field,
            INamedTypeSymbol singleton,
            KnownTypes known,
            string singletonName)
        {
            if (field.IsStatic)
                return "a static field keeps it into the next play session when domain reload is disabled";

            var holder = field.ContainingType;
            if (known.Component == null || holder == null || !DerivesFrom(holder, known.Component))
                return $"'{holder?.Name}' is not a Component, so nothing ties its lifetime to a scene";

            if (IsSerialized(field, known))
                return "a serialized field can write a reference to an edit-mode transient into the scene";

            if (singleton == null || HasSceneLifetime(singleton, known))
                return $"'{singletonName}' has a Scene lifetime, so a scene change destroys it and the " +
                       "field keeps the dead object";

            return null;
        }

        /// <summary>
        /// True when the type or a base carries [SingletonLifetime(Scene)], mirroring the runtime's
        /// inherited attribute lookup. The nearest declaration wins. An unresolved attribute type
        /// reports true, so doubt keeps the warning.
        /// </summary>
        private static bool HasSceneLifetime(INamedTypeSymbol type, KnownTypes known)
        {
            if (known.LifetimeAttribute == null) return true;

            for (var current = type; current != null; current = current.BaseType)
            {
                foreach (var data in current.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(data.AttributeClass, known.LifetimeAttribute)) continue;
                    if (data.ConstructorArguments.Length != 1) return true;

                    var argument = data.ConstructorArguments[0];
                    var scene = argument.Type?.GetMembers(SceneLifetimeMemberName)
                        .OfType<IFieldSymbol>().FirstOrDefault();
                    return scene == null || Equals(argument.Value, scene.ConstantValue);
                }
            }

            return false;
        }

        /// <summary>
        /// Unity's field serialization rule for a reference to a UnityEngine.Object: [SerializeField],
        /// or public and writable without [NonSerialized]. Static fields are handled by the caller.
        /// </summary>
        private static bool IsSerialized(IFieldSymbol field, KnownTypes known)
        {
            if (HasAttribute(field, known.SerializeField)) return true;

            return field.DeclaredAccessibility == Accessibility.Public &&
                   !field.IsReadOnly && !field.IsConst &&
                   !HasAttribute(field, known.NonSerialized);
        }

        /// <summary>True when the symbol carries the given attribute; false when it is unresolved.</summary>
        private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attribute) =>
            attribute != null && symbol.GetAttributes().Any(
                data => SymbolEqualityComparer.Default.Equals(data.AttributeClass, attribute));

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
        private const string OnDestroyName = "OnDestroy";
        private const string OnApplicationQuitName = "OnApplicationQuit";

        /// <summary>
        /// ERS0003: a dereference of Instance inside a teardown callback. Only direct dereferences
        /// are reported — passing the value along, comparing it, or using '?.' is already safe —
        /// and the whole method is exempted when it mentions IsAvailable or TryGetInstance, which
        /// is a deliberately crude but predictable way to honour an explicit guard.
        /// </summary>
        private static bool TryReportTeardown(
            SyntaxNodeAnalysisContext context,
            MemberAccessExpressionSyntax access,
            SyntaxNode enclosing,
            string singletonName)
        {
            if (!(enclosing is MethodDeclarationSyntax method)) return false;

            var methodName = method.Identifier.ValueText;
            if (methodName != OnDestroyName && methodName != OnApplicationQuitName) return false;
            if (!IsDereferenced(access)) return false;

            // Only UnityEngine-declared members reach the native object. Your own methods run on
            // the destroyed component's managed state and are fine, which is the overwhelmingly
            // common teardown shape — an OnDestroy unregistering itself from a manager.
            var member = FindMemberAccessedOnInstance(access);
            if (member == null) return false;

            // Last, because it is the only check here that walks the whole method body.
            if (HasExplicitGuard(method)) return false;

            var symbol = context.SemanticModel.GetSymbolInfo(member, context.CancellationToken).Symbol;
            if (!IsDeclaredByUnity(symbol)) return false;

            var owner = context.SemanticModel
                .GetEnclosingSymbol(access.SpanStart, context.CancellationToken)?.ContainingType;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.UnguardedTeardownAccess,
                access.GetLocation(),
                owner?.Name ?? "<unknown>",
                singletonName,
                member.Identifier.ValueText,
                methodName));
            return true;
        }

        /// <summary>
        /// The name written immediately after Instance: 'Bar' in both 'Foo.Instance.Bar' and
        /// 'Foo.Instance?.Bar'. Null when the expression is something this rule cannot read, such
        /// as an indexer, in which case nothing is reported rather than guessed at.
        /// </summary>
        private static SimpleNameSyntax FindMemberAccessedOnInstance(MemberAccessExpressionSyntax access)
        {
            if (access.Parent is MemberAccessExpressionSyntax plain && plain.Expression == access)
                return plain.Name;

            if (!IsNullConditionalReceiver(access, out var conditional)) return null;

            switch (conditional.WhenNotNull)
            {
                case MemberBindingExpressionSyntax binding:
                    return binding.Name;
                case InvocationExpressionSyntax invocation
                    when invocation.Expression is MemberBindingExpressionSyntax bound:
                    return bound.Name;
                default:
                    return null;
            }
        }

        /// <summary>
        /// True when the symbol is declared by a type in the UnityEngine namespace, which is the
        /// practical test for "backed by the native peer". Checked on the declaring type rather
        /// than the receiver: a user singleton inherits transform from UnityEngine.Component, and
        /// it is the declaration that decides whether the member touches native state.
        ///
        /// Unresolved symbols report false. A missed warning costs what the code cost before this
        /// rule narrowed; a false one is what drives people to switch the rule off.
        /// </summary>
        private static bool IsDeclaredByUnity(ISymbol symbol)
        {
            var declaring = symbol?.ContainingType?.ContainingNamespace;
            if (declaring == null || declaring.IsGlobalNamespace) return false;

            var name = declaring.ToDisplayString();
            return name == "UnityEngine" || name.StartsWith("UnityEngine.", StringComparison.Ordinal);
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
            if (IsNullConditionalReceiver(access, out _)) return true;

            return access.Parent is MemberAccessExpressionSyntax parent && parent.Expression == access;
        }

        /// <summary>
        /// True when this access is the receiver of a '?.' — the 'Foo.Instance' in
        /// 'Foo.Instance?.Bar' — rather than merely sitting somewhere inside one.
        /// </summary>
        private static bool IsNullConditionalReceiver(
            MemberAccessExpressionSyntax access, out ConditionalAccessExpressionSyntax conditional)
        {
            conditional = access.Parent as ConditionalAccessExpressionSyntax;
            return conditional != null && conditional.Expression == access;
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
        // ERS0009 — writes to hideFlags on a singleton or its GameObject
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Reports an assignment, plain or compound, to a UnityEngine-declared hideFlags whose
        /// object is a singleton component or that component's gameObject. Recognised spellings:
        /// 'hideFlags', 'this.hideFlags', 'gameObject.hideFlags' inside a singleton subclass, and
        /// 'X.hideFlags' / 'X.gameObject.hideFlags' where X is typed as a singleton — for example
        /// 'AudioBus.Instance'. A GameObject held in a local or a field is not followed.
        /// </summary>
        private static void AnalyzeHideFlagsAssignment(
            SyntaxNodeAnalysisContext context,
            INamedTypeSymbol singletonBase)
        {
            var left = ((AssignmentExpressionSyntax)context.Node).Left;
            if (NameOf(left)?.Identifier.ValueText != HideFlagsName) return;

            var symbol = context.SemanticModel.GetSymbolInfo(left, context.CancellationToken).Symbol;
            if (!IsDeclaredByUnity(symbol)) return;

            // The hideFlags belong either to the singleton itself or to its gameObject.
            var receiverType = ReceiverTypeOf(context, left);
            string target = null;

            if (receiverType != null && DerivesFrom(receiverType, singletonBase))
            {
                target = $"the singleton '{receiverType.Name}'";
            }
            else if (left is MemberAccessExpressionSyntax access && IsGameObjectAccess(context, access.Expression))
            {
                var ownerType = ReceiverTypeOf(context, access.Expression);
                if (ownerType != null && DerivesFrom(ownerType, singletonBase))
                    target = $"the GameObject of the singleton '{ownerType.Name}'";
            }

            if (target == null) return;

            var enclosing = context.SemanticModel
                .GetEnclosingSymbol(left.SpanStart, context.CancellationToken)?.ContainingType;

            context.ReportDiagnostic(Diagnostic.Create(
                SingletonDiagnostics.HideFlagsAssignment,
                context.Node.GetLocation(),
                enclosing?.Name ?? "<unknown>",
                target));
        }

        /// <summary>The member name of 'x.name' or a bare 'name'; null for anything else.</summary>
        private static SimpleNameSyntax NameOf(ExpressionSyntax expression) =>
            expression is MemberAccessExpressionSyntax access ? access.Name : expression as IdentifierNameSyntax;

        /// <summary>
        /// The static type of the object a member is read from: the type of 'x' in 'x.name', or
        /// the enclosing type for a bare 'name', which reads from 'this'.
        /// </summary>
        private static INamedTypeSymbol ReceiverTypeOf(SyntaxNodeAnalysisContext context, ExpressionSyntax member)
        {
            if (member is MemberAccessExpressionSyntax access)
                return context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type
                    as INamedTypeSymbol;

            return context.SemanticModel
                .GetEnclosingSymbol(member.SpanStart, context.CancellationToken)?.ContainingType;
        }

        /// <summary>True when the expression is UnityEngine's Component.gameObject, bare or qualified.</summary>
        private static bool IsGameObjectAccess(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            if (NameOf(expression)?.Identifier.ValueText != GameObjectName) return false;
            return IsDeclaredByUnity(context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol);
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
        /// type. Comparison is against OriginalDefinition, so MonoBehaviourSingleton&lt;Foo&gt;
        /// matches the unbound MonoBehaviourSingleton&lt;&gt;.
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
