using Jemur.App;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

public sealed class UpdateCoordinatorTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Fact]
    public async Task CheckNowNotifiesOnceForEachNewVersion()
    {
        var checker = new FakeChecker(UpdateCheckResult.Available(Manifest("0.1.4")));
        var notifications = 0;
        using var coordinator = New(checker, (_, _, _) => notifications++);

        await coordinator.CheckNowAsync(CancellationToken.None);
        await coordinator.CheckNowAsync(CancellationToken.None);

        Assert.Equal(1, notifications);
        Assert.Equal(2, checker.Calls);
    }

    [Fact]
    public async Task StagedUpdateSkipsTheNotificationUntilItFallsBack()
    {
        var manifest = Manifest("0.1.4");
        var checker = new FakeChecker(UpdateCheckResult.Available(manifest));
        var notifications = 0;
        using var coordinator = New(checker, (_, _, _) => notifications++);
        coordinator.Stage = _ => true;

        await coordinator.CheckNowAsync(CancellationToken.None);
        Assert.Equal(0, notifications);

        coordinator.NotifyAvailable(manifest);
        coordinator.NotifyAvailable(manifest);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task BackgroundFailureDoesNotNotify()
    {
        var checker = new FakeChecker(UpdateCheckResult.Failed("offline"));
        var notifications = 0;
        using var coordinator = New(checker, (_, _, _) => notifications++);

        var result = await coordinator.CheckNowAsync(CancellationToken.None);

        Assert.Equal(UpdateCheckKind.Failed, result.Kind);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task StartWaitsForInitialDelayBeforeChecking()
    {
        var checker = new FakeChecker(UpdateCheckResult.Available(Manifest("0.1.4")));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delays = new List<TimeSpan>();
        using var coordinator = New(checker, (_, _, _) => { }, async (delay, token) =>
        {
            delays.Add(delay);
            if (delays.Count == 1) await release.Task;
            else await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });

        coordinator.Start();
        await Task.Delay(20);
        Assert.Equal(0, checker.Calls);
        release.SetResult();
        await WaitUntil(() => checker.Calls == 1);
        Assert.Equal(TimeSpan.FromSeconds(30), delays[0]);
    }

    [Fact]
    public async Task DisposeCancelsTheBackgroundLoop()
    {
        var checker = new FakeChecker(UpdateCheckResult.Available(Manifest("0.1.4")));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = New(checker, (_, _, _) => { }, async (_, token) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });

        coordinator.Start();
        await started.Task;
        coordinator.Dispose();
        Assert.Equal(0, checker.Calls);
    }

    private static UpdateCoordinator New(FakeChecker checker, Action<string, string, bool> notify, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        delay == null
            ? new UpdateCoordinator(checker, notify, () => { }, Clock, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24))
            : new UpdateCoordinator(checker, notify, () => { }, Clock, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24), delay);

    private static UpdateManifest Manifest(string version) => new(version, "0123456789012345678901234567890123456789", new Uri($"https://github.com/revanapriyandi/jemur/releases/tag/v{version}"));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class FakeChecker(UpdateCheckResult result) : IUpdateChecker
    {
        public int Calls { get; private set; }
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
