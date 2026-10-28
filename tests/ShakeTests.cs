using Clothesline.Core;
using Xunit;

namespace Clothesline.Tests;

public sealed class ShakeTests
{
    private static bool Run(Shake s, double[] xs, int stepMs)
    {
        bool shaken = false;
        for (int i = 0; i < xs.Length; i++) shaken |= s.Add(xs[i], i * stepMs);
        return shaken;
    }

    private static readonly double[] BackAndForth = { 0, 40, 0, 40, 0, 40, 0 };

    [Fact]
    public void QuickBackAndForth_IsAShake() => Assert.True(Run(new Shake(), BackAndForth, 60));

    [Fact]
    public void SlowBackAndForth_IsNot() => Assert.False(Run(new Shake(), BackAndForth, 400));

    [Fact]
    public void SmallWiggle_IsNot() => Assert.False(Run(new Shake(), new double[] { 0, 10, 0, 10, 0, 10, 0, 10 }, 40));

    [Fact]
    public void LongDragOneWay_IsNot() => Assert.False(Run(new Shake(), Enumerable.Range(0, 30).Select(i => i * 30d).ToArray(), 20));
}
