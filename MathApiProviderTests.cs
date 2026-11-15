using Elektra.Knowledge.Providers;

namespace Elektra.Tests;

[TestClass]
public sealed class MathApiProviderTests
{
    private readonly MathApiProvider _provider = new();

    [TestMethod]
    public async Task TryResolveAsync_EvaluatesPrecedenceAndParentheses()
    {
        var response = await _provider.TryResolveAsync("(2 + 3) * 4", CancellationToken.None);

        Assert.IsNotNull(response);
        Assert.AreEqual("20", response.Answer);
        Assert.AreEqual("Basic arithmetic", response.Source);
    }

    [TestMethod]
    public async Task TryResolveAsync_UsesInvariantDecimalFormatting()
    {
        var response = await _provider.TryResolveAsync("1 / 4", CancellationToken.None);

        Assert.IsNotNull(response);
        Assert.AreEqual("0.25", response.Answer);
    }

    [TestMethod]
    public async Task TryResolveAsync_ReturnsNullForUnsupportedInput()
    {
        var response = await _provider.TryResolveAsync("what time is it", CancellationToken.None);

        Assert.IsNull(response);
    }

    [TestMethod]
    public async Task TryResolveAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await _provider.TryResolveAsync("2 + 2", cancellation.Token));
    }
}
