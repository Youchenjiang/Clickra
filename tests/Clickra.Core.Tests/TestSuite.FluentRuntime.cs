using Clickra.Core;

namespace Clickra.Core.Tests;

static partial class TestSuite
{
    public static void RegisterFluentRuntimeTests(TestRunner runner)
    {
        runner.Run("Fluent Store add-on accepts only the Store publisher", TestFluentStorePublisherGate);
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
}
