using Clickra.Core;
using System.Xml.Linq;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterFluentRuntimeTests(TestRunner runner)
    {
        runner.Run("Fluent Store add-on accepts only the Store publisher", TestFluentStorePublisherGate);
        runner.Run("Fluent Store publisher matches the package manifest", TestFluentStorePublisherMatchesManifest);
    }

    private static void TestFluentStorePublisherGate()
    {
        Assert.True(FluentRuntimeHelper.IsStorePublisher(FluentRuntimeHelper.StorePublisher),
            "The exact Microsoft Store Publisher must enable the Fluent Store add-on.");
        Assert.True(!FluentRuntimeHelper.IsStorePublisher("CN=Clickra GitHub Production"),
            "A GitHub direct-download Publisher must not enable the Store-owned Fluent add-on.");
        Assert.True(!FluentRuntimeHelper.IsStorePublisher(null),
            "A missing Publisher must fail closed.");
        Assert.True(!FluentRuntimeHelper.IsStorePublisher(""),
            "An empty Publisher must fail closed.");
        Assert.True(!FluentRuntimeHelper.IsStorePublisher(FluentRuntimeHelper.StorePublisher.ToLowerInvariant()),
            "Publisher matching must remain exact so a different package identity cannot pass the gate.");
    }

    private static void TestFluentStorePublisherMatchesManifest()
    {
        string? root = FindRepoRoot();
        Assert.True(root is not null, "The repository root must be discoverable for the Store Publisher consistency test.");

        string manifestPath = Path.Combine(root!, "packaging", "msix", "AppxManifest.xml");
        var document = XDocument.Load(manifestPath);
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        string? manifestPublisher = document.Root?
            .Element(ns + "Identity")?
            .Attribute("Publisher")?
            .Value;

        Assert.True(
            string.Equals(manifestPublisher, FluentRuntimeHelper.StorePublisher, StringComparison.Ordinal),
            $"FluentRuntimeHelper.StorePublisher must exactly match the Store package manifest Publisher. Manifest='{manifestPublisher}', Constant='{FluentRuntimeHelper.StorePublisher}'.");
    }
}
