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
        /// ERS0001 — an Awake or OnDestroy override that never calls its base implementation.
        /// The base Awake claims the singleton slot and destroys duplicates; the base OnDestroy
        /// releases it. Skipping either leaves a singleton that is never registered, or a stale
        /// static reference to a destroyed object.
        /// </summary>
        public static readonly DiagnosticDescriptor MissingBaseCall = new DiagnosticDescriptor(
            id: "ERS0001",
            title: "Singleton message override must call its base implementation",
            messageFormat: "'{0}.{1}' overrides the singleton's '{1}' but never calls base.{1}(); " +
                           "the slot will not be claimed or released",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The singleton base classes do their registration, deduplication and " +
                         "teardown work inside Awake and OnDestroy. An override that does not " +
                         "chain to base leaves the singleton non-functional in a way that only " +
                         "shows up at runtime.",
            helpLinkUri: HelpBase + "ers0001");

        /// <summary>
        /// ERS0002 — storing Instance in a field. Bypasses the session guard and the fake-null
        /// collapse, which are the two mechanisms that make the accessor safe.
        /// </summary>
        public static readonly DiagnosticDescriptor CachedInstance = new DiagnosticDescriptor(
            id: "ERS0002",
            title: "Do not cache a singleton Instance in a field",
            messageFormat: "'{0}' stores '{1}.Instance' in a field; read the property at the point " +
                           "of use instead",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "The Instance accessor discards references captured in a previous play " +
                         "session and collapses Unity's destroyed-object wrapper into a real null. " +
                         "A field copy does neither, so it survives domain reload and scene " +
                         "changes as a reference to an object that no longer exists. A local " +
                         "variable inside a single method is fine.",
            helpLinkUri: HelpBase + "ers0002");

        /// <summary>
        /// ERS0003 — dereferencing Instance during teardown, where the contract allows null.
        /// </summary>
        public static readonly DiagnosticDescriptor UnguardedTeardownAccess = new DiagnosticDescriptor(
            id: "ERS0003",
            title: "Guard singleton access in teardown callbacks",
            messageFormat: "'{0}' dereferences '{1}.Instance' inside '{2}', where it is allowed to " +
                           "be null; test IsAvailable, use TryGetInstance, or use '?.'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Instance is non-null for the whole time the application is running and " +
                         "null only once shutdown has begun, because recreating a singleton during " +
                         "teardown leaks objects into an unloading scene and can touch subsystems " +
                         "that have already shut down.",
            helpLinkUri: HelpBase + "ers0003");

        /// <summary>
        /// ERS0004 — touching Instance from an instance field initializer or a constructor of a
        /// MonoBehaviour. Unity throws on Find and GameObject construction in both contexts.
        /// </summary>
        public static readonly DiagnosticDescriptor ConstructionTimeAccess = new DiagnosticDescriptor(
            id: "ERS0004",
            title: "Do not access a singleton during MonoBehaviour construction",
            messageFormat: "'{0}' reads '{1}.Instance' from {2}; Unity does not permit object " +
                           "lookup or creation before Awake",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Field initializers and constructors on a MonoBehaviour run on Unity's " +
                         "deserialization path, where FindObjectsByType and new GameObject throw. " +
                         "Move the access to Awake, OnEnable or Start.",
            helpLinkUri: HelpBase + "ers0004");

        /// <summary>
        /// ERS0005 — declaring Awake/OnDestroy without 'override' in a singleton subclass, which
        /// hides the base method. Unity invokes the most-derived declaration, so the base logic
        /// silently never runs — the same end state as ERS0001, reached by a different mistake.
        /// </summary>
        public static readonly DiagnosticDescriptor HidesBaseMessage = new DiagnosticDescriptor(
            id: "ERS0005",
            title: "Singleton message must be declared with 'override'",
            messageFormat: "'{0}.{1}' hides the singleton's virtual '{1}'; declare it as " +
                           "'protected override void {1}()' and call base",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Unity calls the most-derived declaration of a magic method by name. A " +
                         "non-override declaration compiles with only a hiding warning, but the " +
                         "base registration and teardown never run.",
            helpLinkUri: HelpBase + "ers0005");
    }
}
