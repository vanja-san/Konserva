using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests;

/// <summary>
/// Covers <see cref="LoaderVersionSelector"/> — the "only offer newer loader
/// versions" rule behind the loader update card.
/// </summary>
[Trait("Category", "Unit")]
public class LoaderVersionSelectorTests
{
    private static readonly string[] AllVersions =
        ["0.19.3", "0.19.4", "0.19.5", "0.20.0", "1.0.0"];

    [Fact]
    public void GetSelectableVersions_KeepsOnlyNewerThanCurrent()
    {
        var result = LoaderVersionSelector.GetSelectableVersions(AllVersions, "0.19.4");

        Assert.Equal(["0.19.5", "0.20.0", "1.0.0"], result);
    }

    /// <summary>
    /// The current version itself must not be offered as an update — reinstalling
    /// the same build would look like a successful update while changing nothing.
    /// </summary>
    [Fact]
    public void GetSelectableVersions_ExcludesCurrentVersion()
    {
        var result = LoaderVersionSelector.GetSelectableVersions(AllVersions, "1.0.0");

        Assert.DoesNotContain("1.0.0", result);
    }

    /// <summary>
    /// A server without a recorded loader version has nothing to compare against,
    /// so every known build stays selectable.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetSelectableVersions_ReturnsWholeListWhenCurrentUnknown(string? current)
    {
        var result = LoaderVersionSelector.GetSelectableVersions(AllVersions, current);

        Assert.Equal(AllVersions, result);
    }

    [Fact]
    public void GetSelectableVersions_ReturnsEmptyWhenAlreadyLatest()
    {
        var result = LoaderVersionSelector.GetSelectableVersions(AllVersions, "1.0.0");

        Assert.Empty(result);
    }

    [Fact]
    public void GetSelectableVersions_ReturnsEmptyForEmptyList()
    {
        var result = LoaderVersionSelector.GetSelectableVersions([], "0.19.4");

        Assert.Empty(result);
    }

    /// <summary>
    /// Guards the numeric-component comparison: a naive string compare would treat
    /// "0.9.0" as newer than "0.10.0".
    /// </summary>
    [Fact]
    public void GetSelectableVersions_ComparesNumericallyNotAsText()
    {
        var result = LoaderVersionSelector.GetSelectableVersions(["0.9.0", "0.10.0"], "0.9.0");

        Assert.Equal(["0.10.0"], result);
    }

    [Fact]
    public void GetSelectableVersions_PreservesOriginalOrder()
    {
        var result = LoaderVersionSelector.GetSelectableVersions(AllVersions, "0.19.3");

        Assert.Equal(AllVersions.Skip(1).ToArray(), result);
    }

    [Fact]
    public void HasUpdates_TrueWhenNewerVersionExists()
    {
        Assert.True(LoaderVersionSelector.HasUpdates(AllVersions, "0.19.4"));
    }

    [Fact]
    public void HasUpdates_FalseWhenAlreadyOnLatest()
    {
        Assert.False(LoaderVersionSelector.HasUpdates(AllVersions, "1.0.0"));
    }

    [Fact]
    public void HasUpdates_FalseForEmptyList()
    {
        Assert.False(LoaderVersionSelector.HasUpdates([], "0.19.4"));
    }

    [Fact]
    public void HasUpdates_TrueWhenCurrentUnknownAndListNotEmpty()
    {
        Assert.True(LoaderVersionSelector.HasUpdates(AllVersions, null));
    }

    [Fact]
    public void HasUpdates_FalseWhenCurrentUnknownAndListEmpty()
    {
        Assert.False(LoaderVersionSelector.HasUpdates([], null));
    }

    /// <summary>
    /// Both entry points must agree, otherwise the card would offer a version the
    /// header notification does not mention, or vice versa.
    /// </summary>
    [Theory]
    [InlineData("0.19.3")]
    [InlineData("0.19.5")]
    [InlineData("1.0.0")]
    [InlineData("9.9.9")]
    public void HasUpdates_MatchesGetSelectableVersions(string current)
    {
        var selectable = LoaderVersionSelector.GetSelectableVersions(AllVersions, current);

        Assert.Equal(selectable.Length > 0, LoaderVersionSelector.HasUpdates(AllVersions, current));
    }

    [Fact]
    public void HasUpdates_HandlesPaperBuildWithSuffix()
    {
        string[] paper = ["492 (ALPHA)", "493"];

        Assert.True(LoaderVersionSelector.HasUpdates(paper, "492 (ALPHA)"));
        Assert.False(LoaderVersionSelector.HasUpdates(paper, "493"));
    }

    [Fact]
    public void GetSelectableVersions_ThrowsOnNullList()
    {
        string[]? noVersions = null;

        Assert.Throws<ArgumentNullException>(() =>
            LoaderVersionSelector.GetSelectableVersions(noVersions!, "1.0.0"));
    }
}