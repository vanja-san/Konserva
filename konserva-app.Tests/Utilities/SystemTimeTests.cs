using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests.Utilities;

/// <summary>
/// Тесты для SystemTime — обёртки над TimeProvider.
/// </summary>
public class SystemTimeTests
{
    [Fact]
    public void Now_ReturnsCurrentLocalTime()
    {
        var before = DateTime.Now;
        var now = SystemTime.Now;
        var after = DateTime.Now;

        Assert.InRange(now, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var before = DateTime.UtcNow;
        var utcNow = SystemTime.UtcNow;
        var after = DateTime.UtcNow;

        Assert.InRange(utcNow, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void Provider_ReturnsSystemProvider_ByDefault()
    {
        Assert.Same(TimeProvider.System, SystemTime.Provider);
    }

    [Fact]
    public void SetProvider_ChangesTimeProvider()
    {
        var mockProvider = new MockTimeProvider(new DateTime(2025, 1, 15, 12, 30, 0, DateTimeKind.Utc));
        SystemTime.SetProvider(mockProvider);

        try
        {
            Assert.Equal(new DateTime(2025, 1, 15, 12, 30, 0), SystemTime.UtcNow);
        }
        finally
        {
            SystemTime.Reset();
        }
    }

    [Fact]
    public void SetProvider_WithMock_NowReturnsMockLocalTime()
    {
        var mockUtc = new DateTime(2025, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var mockProvider = new MockTimeProvider(mockUtc);
        SystemTime.SetProvider(mockProvider);

        try
        {
            var expectedLocal = mockUtc.ToLocalTime();
            Assert.Equal(expectedLocal, SystemTime.Now);
        }
        finally
        {
            SystemTime.Reset();
        }
    }

    [Fact]
    public void SetProvider_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => SystemTime.SetProvider(null!));
    }

    [Fact]
    public void Reset_RestoresSystemProvider()
    {
        var mockProvider = new MockTimeProvider(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        SystemTime.SetProvider(mockProvider);
        SystemTime.Reset();

        Assert.Same(TimeProvider.System, SystemTime.Provider);
    }

    /// <summary>
    /// Простая реализация TimeProvider для тестов.
    /// </summary>
    private sealed class MockTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public MockTimeProvider(DateTime utcNow)
        {
            _utcNow = new DateTimeOffset(utcNow, TimeSpan.Zero);
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
