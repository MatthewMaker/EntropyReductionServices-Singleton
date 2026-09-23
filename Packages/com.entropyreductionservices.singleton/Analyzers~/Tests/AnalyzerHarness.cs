using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

using EntropyReductionServices.Analyzers;

namespace ERS.Singleton.Analyzers.Tests
{
    /// <summary>
    /// Compiles a snippet against stub Unity and singleton types and checks which diagnostics the
    /// analyzer produces.
    ///
    /// The stubs are source rather than a reference to the real assemblies so the tests run
    /// without Unity installed. They mirror the shape the analyzer actually inspects: which type
    /// declares Instance, which declares the virtual Awake, and which inherits from
    /// UnityEngine.MonoBehaviour. Getting that shape wrong would make the tests agree with
    /// themselves and with nothing else.
    /// </summary>
    internal static class Harness
    {
        /// <summary>
        /// The stub Unity and singleton hierarchy, read from the embedded copy of
        /// Shared/SingletonStubs.cs — the same file StalenessProbe compiles — plus the consuming
        /// types the snippets refer to. Sharing the hierarchy is what stops the probe and these
        /// tests drifting into modelling different shapes.
        /// </summary>
        private static readonly string Prelude = ReadStubs() + @"
namespace Consuming
{
    using EntropyReductionServices.Singletons;

    public class Bus : MonoBehaviourSingletonPersistent<Bus>
    {
        public void Stop() { }
    }

    // Passive: Instance is null until an Awake claims the slot, so '?.' on it is a real guard.
    public class Board : MonoBehaviourSingletonPassive<Board>
    {
        public void Stop() { }
    }
}
";

        /// <summary>Reads the shared stub source embedded by the csproj.</summary>
        private static string ReadStubs()
        {
            var assembly = typeof(Harness).Assembly;
            using (var stream = assembly.GetManifestResourceStream("SingletonStubs.cs"))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "SingletonStubs.cs is not embedded; check the EmbeddedResource item in the test csproj.");

                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Runs the analyzer over the snippet. Expected diagnostics are written inline as
        /// {|ERS0001:span|} markup, which keeps them anchored to the code rather than to line
        /// numbers that shift whenever the prelude changes.
        /// </summary>
        public static Task Verify(string snippet)
        {
            var test = new CSharpAnalyzerTest<SingletonAnalyzer, DefaultVerifier>
            {
                TestCode = Prelude + snippet,
            };

            return test.RunAsync();
        }
    }
}
