using System;
using System.IO;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterGitPolicyTests(TestRunner runner)
    {
        runner.Run("Git policy guard rejects fixture identities before commit", () =>
        {
            string root = FindRepoRoot() ?? throw new TestSkippedException("Could not locate the repository root.");
            string hook = File.ReadAllText(Path.Combine(root, "scripts", "hooks", "commit-msg"));

            Assert.True(hook.Contains("git config --get user.email", StringComparison.Ordinal),
                "commit-msg must inspect the effective Git email before the commit object is created.");
            Assert.True(hook.Contains("*.invalid", StringComparison.Ordinal),
                "commit-msg must reject the reserved .invalid domain used by throwaway test repositories.");
            Assert.True(hook.Contains("Refusing to commit with a fixture/invalid Git identity", StringComparison.Ordinal),
                "The identity failure must explain why the commit was blocked.");
        });
    }
}