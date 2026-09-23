using Microsoft.CodeAnalysis;

namespace EntropyReductionServices.Analyzers
{
    /// <summary>
    /// Descriptors for the rules that enforce the MonoBehaviourSingleton contract.
    ///
    /// Severity rationale: every rule ships as a Warning. ERS0001 and ERS0004 describe hard
    /// breakage and would justify Error, but these rules arrive with a consumer's first reference
    /// to the package rather than by opt-in, so a false positive would be a broken build for
    /// someone who never asked for linting. Escalate locally with a Default.ruleset — see
    /// Documentation~/analyzers.md — and revisit the defaults once there is field evidence.
    /// </summary>
    internal static class SingletonDiagnostics
    {
        private const string Category = "Singleton";

        // Baked into the shipped DLL and surfaced as the "learn more" link in Rider, Visual
        // Studio and the Unity Console. Changing the repository name or the docs path means
        // rebuilding and recommitting the analyzer, not just editing markdown.
        private const string HelpBase =
            "https://github.com/MatthewMaker/EntropyReductionServices-Singleton/blob/main/" +
            "Packages/com.entropyreductionservices.singleton/Documentation~/analyzers.md#";

        /// <summary>
        /// Builds a descriptor with the uniform parts filled in. The help anchor is derived from
        /// the id rather than typed alongside it, so the two cannot drift apart, and the
        /// "every rule ships as a Warning" policy above has exactly one line of code behind it.
        /// </summary>
        private static DiagnosticDescriptor Rule(
            string id, string title, string messageFormat, string description) =>
            new DiagnosticDescriptor(
                id: id,
                title: title,
                messageFormat: messageFormat,
                category: Category,
                defaultSeverity: DiagnosticSeverity.Warning,
                isEnabledByDefault: true,
                description: description,
                helpLinkUri: HelpBase + id.ToLowerInvariant());

        /// <summary>
        /// ERS0001 — an Awake or OnDestroy override that never calls its base implementation.
        /// The base Awake claims the singleton slot and destroys duplicates; the base OnDestroy
        /// releases it. Skipping either leaves a singleton that is never registered, or a stale
        /// static reference to a destroyed object.
        /// </summary>
        public static readonly DiagnosticDescriptor MissingBaseCall = Rule(
            id: "ERS0001",
            title: "Singleton message override must call its base implementation",
            messageFormat: "'{0}.{1}' overrides the singleton's '{1}' but never calls base.{1}(); " +
                           "the slot will not be claimed or released",
            description: "The singleton base classes do their registration, deduplication and " +
                         "teardown work inside Awake and OnDestroy. An override that does not " +
                         "chain to base leaves the singleton non-functional in a way that only " +
                         "shows up at runtime.");

        /// <summary>
        /// ERS0002 — storing Instance in a field. Bypasses the session guard and the fake-null
        /// collapse, which are the two mechanisms that make the accessor safe.
        /// </summary>
        public static readonly DiagnosticDescriptor CachedInstance = Rule(
            id: "ERS0002",
            title: "Do not cache a singleton Instance in a field",
            messageFormat: "'{0}' stores '{1}.Instance' in a field; read the property at the point " +
                           "of use instead",
            description: "The Instance accessor discards references captured in a previous play " +
                         "session and collapses Unity's destroyed-object wrapper into a real null. " +
                         "A field copy does neither, so it survives domain reload and scene " +
                         "changes as a reference to an object that no longer exists. A local " +
                         "variable inside a single method is fine.");

        /// <summary>
        /// ERS0003 — dereferencing Instance during teardown, where the contract allows null.
        /// </summary>
        public static readonly DiagnosticDescriptor UnguardedTeardownAccess = Rule(
            id: "ERS0003",
            title: "Guard teardown access to a singleton's Unity members",
            messageFormat: "'{0}' reads '{1}.Instance.{2}' inside '{3}'; '{2}' is declared by " +
                           "UnityEngine, so it reaches a native object that may already be " +
                           "destroyed. Test IsAvailable or use TryGetInstance",
            description: "During teardown Instance returns the component that held the slot, " +
                         "still a live C# object after its native peer is gone. Calling your own " +
                         "members on it is therefore safe, and is not reported: an OnDestroy that " +
                         "unregisters itself from a manager needs no guard. Members declared by " +
                         "UnityEngine are the exception — transform, gameObject, enabled, " +
                         "StartCoroutine and the rest reach the native object and raise " +
                         "MissingReferenceException. Note that '?.' is not a guard: it tests the " +
                         "reference, and the destroyed component is a live C# object, so it " +
                         "proceeds. Use IsAvailable or TryGetInstance, which consult Unity's == " +
                         "overload. The rule reads only the member named directly on Instance; a " +
                         "method of your own that itself touches the native peer is invisible to it.");

        /// <summary>
        /// ERS0004 — touching Instance from an instance field initializer or a constructor of a
        /// MonoBehaviour. Unity throws on Find and GameObject construction in both contexts.
        /// </summary>
        public static readonly DiagnosticDescriptor ConstructionTimeAccess = Rule(
            id: "ERS0004",
            title: "Do not access a singleton during MonoBehaviour construction",
            messageFormat: "'{0}' reads '{1}.Instance' from {2}; Unity does not permit object " +
                           "lookup or creation before Awake",
            description: "Field initializers and constructors on a MonoBehaviour run on Unity's " +
                         "deserialization path, where FindObjectsByType and new GameObject throw. " +
                         "Move the access to Awake, OnEnable or Start.");

        /// <summary>
        /// ERS0007 — the base call is present but in the wrong position.
        ///
        /// ERS0001 only asks whether base.Awake() / base.OnDestroy() is called at all, so both of
        /// these pass it and are still wrong:
        ///
        ///   Awake:     work happens before base.Awake() claims the slot, so a duplicate that is
        ///              about to destroy itself runs that work first, side effects included.
        ///   OnDestroy: cleanup happens after base.OnDestroy() releases the slot, so Instance is
        ///              gone and IsCurrentInstance has already turned false.
        ///
        /// Order-dependent, silent, and invisible to every other rule.
        /// </summary>
        public static readonly DiagnosticDescriptor BaseCallOutOfOrder = Rule(
            id: "ERS0007",
            title: "Singleton base call is in the wrong position",
            messageFormat: "'{0}.{1}' must call base.{1}() {2}",
            description: "The base Awake claims the singleton slot and destroys duplicates, so it " +
                         "belongs before anything else: work placed ahead of it runs even on an " +
                         "instance that is about to destroy itself. The base OnDestroy releases " +
                         "the slot, so it belongs after everything else: code placed behind it " +
                         "sees no live instance and an IsCurrentInstance that has already gone " +
                         "false. Calling the base at all satisfies ERS0001; calling it in the " +
                         "right place is a separate question.");

        /// <summary>
        /// ERS0006 — '?.' on a lazy singleton's Instance outside teardown. The accessor cannot
        /// return null there, so the operator is dead; and inside teardown it does not protect
        /// anything, which ERS0003 covers. Either way it tells a reader the value may be null,
        /// which for this flavour is never the useful thing to believe.
        ///
        /// Deliberately silent for the passive flavours, whose Instance IS null until an Awake
        /// claims the slot. There '?.' is a correct guard, and flagging it would fire hardest on
        /// the one flavour that needs it.
        /// </summary>
        public static readonly DiagnosticDescriptor RedundantNullConditional = Rule(
            id: "ERS0006",
            title: "Null-conditional access on a lazy singleton's Instance is misleading",
            messageFormat: "'{0}.Instance' cannot be null here, so '?.' is dead; it also does not " +
                           "guard teardown. Dereference it directly",
            description: "A lazy singleton's Instance resolves, creates, or throws — it does not " +
                         "return null while the application is running, and during teardown it " +
                         "returns the destroyed component, which '?.' does not stop because the " +
                         "operator tests the reference rather than Unity's == overload. The " +
                         "operator therefore never does what it appears to do on this flavour. " +
                         "Passive singletons are a different case and are not reported: their " +
                         "Instance is null until a component's Awake claims the slot.");

        /// <summary>
        /// ERS0005 — declaring Awake/OnDestroy without 'override' in a singleton subclass, which
        /// hides the base method. Unity invokes the most-derived declaration, so the base logic
        /// silently never runs — the same end state as ERS0001, reached by a different mistake.
        /// </summary>
        public static readonly DiagnosticDescriptor HidesBaseMessage = Rule(
            id: "ERS0005",
            title: "Singleton message must be declared with 'override'",
            messageFormat: "'{0}.{1}' hides the singleton's virtual '{1}'; declare it as " +
                           "'protected override void {1}()' and call base",
            description: "Unity calls the most-derived declaration of a magic method by name. A " +
                         "non-override declaration compiles with only a hiding warning, but the " +
                         "base registration and teardown never run.");
    }
}
