using System.Windows;
using Jemur.Core;
using Xunit;

namespace Jemur.Tests;

public sealed class LineLayoutTests
{
    private static readonly double[] W = { 50, 70 };

    [Fact]
    public void GroupSlots_ReturnsCardCentres() =>
        Assert.Equal(new[] { 125d, 195d }, LineLayout.GroupSlots(100, W, 10));

    [Theory]
    [InlineData(110, 0)]
    [InlineData(170, 1)]
    [InlineData(400, 2)]
    public void SlotIndexAt_FindsInsertIndex(double x, int expected) =>
        Assert.Equal(expected, LineLayout.SlotIndexAt(100, W, 10, x));

    [Fact]
    public void IsOutsideGroup_NeedsHalfACardBeyondAnEdge()
    {
        Assert.True(LineLayout.IsOutsideGroup(100, W, 10, 50, 50));
        Assert.False(LineLayout.IsOutsideGroup(100, W, 10, 150, 50));
        Assert.True(LineLayout.IsOutsideGroup(100, W, 10, 300, 50));
        Assert.False(LineLayout.IsOutsideGroup(100, W, 10, 250, 50));
    }

    [Fact]
    public void StackSlots_StepRightAndAlternateTilt()
    {
        Assert.Equal((0d, -3d), LineLayout.StackSlot(0));
        Assert.Equal((4d, 3d), LineLayout.StackSlot(1));
        Assert.Equal((8d, -3d), LineLayout.StackSlot(2));
    }

    [Fact]
    public void PushPast_LeavesClearCardsAlone() =>
        Assert.Equal(50, LineLayout.PushPast(50, 20, new[] { (100d, 300d) }, 10));

    [Fact]
    public void PushPast_MovesAnOverlappingCardJustPastTheRightEdge() =>
        Assert.Equal(330, LineLayout.PushPast(280, 20, new[] { (100d, 300d) }, 10));

    [Fact]
    public void PushPast_SendsACardOnTheLeftHalfOutToTheLeft() =>
        Assert.Equal(70, LineLayout.PushPast(110, 20, new[] { (100d, 300d) }, 10));

    [Fact]
    public void PushPast_ClearsEveryGroupItLandsIn() =>
        Assert.Equal(530, LineLayout.PushPast(250, 20, new[] { (320d, 500d), (100d, 300d) }, 10));

    [Fact]
    public void PushSpanPast_MovesAnOverlappingGroupBesideTheOther()
    {
        var other = new[] { (100d, 300d) };
        Assert.Equal(310, LineLayout.PushSpanPast(290, 400, other, 10));  // mostly right of it: past its right edge
        Assert.Equal(-10, LineLayout.PushSpanPast(50, 150, other, 10));   // mostly left of it: past its left edge
        Assert.Equal(320, LineLayout.PushSpanPast(320, 400, other, 10)); // clear: stays
    }

    private static readonly Rect[] Screens = { new(0, 0, 1920, 1040), new(1920, 0, 1280, 984) };

    [Fact]
    public void Clamp_MovesOffscreenPinIntoNearestWorkArea() =>
        Assert.Equal(new Point(3040, 100), LineLayout.ClampPin(new Rect(3500, 100, 160, 140), Screens));

    [Fact]
    public void Clamp_KeepsMostlyVisiblePin() =>
        Assert.Equal(new Point(1850, 100), LineLayout.ClampPin(new Rect(1850, 100, 160, 140), Screens));

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.75)]
    public void CardAndWindowOffsetsRoundTrip(double scale)
    {
        var card = new Point(2400, 300);
        var pad = new Vector(92, 28);
        var window = LineLayout.WindowForCard(card, pad, scale);
        Assert.Equal(new Point(2400 - 92 * scale, 300 - 28 * scale), window);
        Assert.Equal(card, LineLayout.CardInWindow(window, pad, scale));
    }

    [Fact]
    public void DraggedCard_StaysUnderTheCursorWhenTheScaleChanges()
    {
        var pad = new Vector(92, 28);
        var pressCard = new Point(1700, 300);
        var pressCursor = new Point(1780, 320); // grabbed 80 px right, 20 px down from the card's top-left
        var cursor = new Point(2100, 340);      // now on a 150% monitor
        var card = LineLayout.DraggedCard(pressCard, pressCursor, cursor);
        Assert.Equal(new Vector(80, 20), pressCursor - pressCard);
        Assert.Equal(new Vector(80, 20), cursor - card);
        foreach (var scale in new[] { 1.0, 1.5 })
            Assert.Equal(card, LineLayout.CardInWindow(LineLayout.WindowForCard(card, pad, scale), pad, scale));
    }

    private static readonly Rect Area = new(0, 0, 1920, 1040);
    private static readonly Size Card = new(160, 140);

    [Fact]
    public void FreeDesktopSpot_FirstSpotIsTopLeftWithMargin() =>
        Assert.Equal(new Point(32, 32), LineLayout.FreeDesktopSpot(Area, Card, Array.Empty<Rect>()));

    [Fact]
    public void FreeDesktopSpot_SkipsOccupiedAndWrapsRows()
    {
        Assert.Equal(new Point(208, 32), LineLayout.FreeDesktopSpot(Area, Card, new[] { new Rect(32, 32, 160, 140) }));
        var row = new List<Rect>();
        for (double x = 32; x + 160 <= 1920 - 32; x += 176) row.Add(new Rect(x, 32, 160, 140));
        Assert.Equal(new Point(32, 32 + 140 + 16), LineLayout.FreeDesktopSpot(Area, Card, row));
    }

    [Fact]
    public void FreeDesktopSpot_WrapsWithOffsetWhenFull()
    {
        var all = new List<Rect>();
        for (double y = 32; y + 140 <= 1040 - 32; y += 156)
            for (double x = 32; x + 160 <= 1920 - 32; x += 176) all.Add(new Rect(x, y, 160, 140));
        Assert.Equal(new Point(56, 56), LineLayout.FreeDesktopSpot(Area, Card, all));
    }

    [Fact]
    public void ClampCardSize_NoteLimits()
    {
        Assert.Equal(new Size(120, 100), LineLayout.ClampCardSize(false, new Size(50, 50), 1));
        Assert.Equal(new Size(640, 640), LineLayout.ClampCardSize(false, new Size(900, 900), 1));
    }

    [Fact]
    public void ClampCardSize_ImageKeepsAspect()
    {
        Assert.Equal(new Size(960, 480), LineLayout.ClampCardSize(true, new Size(2000, 5), 2));
        Assert.Equal(new Size(120, 60), LineLayout.ClampCardSize(true, new Size(10, 5), 2));
    }

    [Fact]
    public void ClampCardSize_PortraitImageIsCappedAt960Tall()
    {
        var s = LineLayout.ClampCardSize(true, new Size(2000, 5), 9 / 19.0);
        Assert.True(s.Height <= 960 + 1e-9);
        Assert.Equal(960, s.Height, 6);
        Assert.Equal(960 * 9 / 19.0, s.Width, 6);
        Assert.Equal(new Size(600, 960), LineLayout.ClampCardSize(true, new Size(600, 5), 0.625)); // exactly at the cap
    }

    [Fact]
    public void ClampCardSize_VeryTallImageKeepsHeightCapOverMinWidth() =>
        Assert.Equal(960, LineLayout.ClampCardSize(true, new Size(120, 5), 0.1).Height, 6);

    [Fact]
    public void FreeDesktopSpot_ScalesMarginAndGapsToPixels()
    {
        var area = new Rect(0, 0, 2880, 1560); // 150%
        var card = new Size(240, 210);
        Assert.Equal(new Point(48, 48), LineLayout.FreeDesktopSpot(area, card, Array.Empty<Rect>(), 1.5));
        Assert.Equal(new Point(48 + 240 + 24, 48), LineLayout.FreeDesktopSpot(area, card, new[] { new Rect(48, 48, 240, 210) }, 1.5));
    }

    [Fact]
    public void ClampCardSize_BadAspectFallsBack()
    {
        Assert.Equal(new Size(160, 90), LineLayout.ClampCardSize(true, new Size(160, 5), 0));
        Assert.Equal(new Size(160, 90), LineLayout.ClampCardSize(true, new Size(160, 5), double.NaN));
    }

    [Theory]
    [InlineData(0.85)]
    [InlineData(1.0)]
    [InlineData(1.22)]
    public void ShownSize_ScalesStoredAndRoundTrips(double scale)
    {
        var stored = new Size(200, 150);
        var shown = LineLayout.ShownSize(stored, scale);
        Assert.Equal(new Size(Math.Round(200 * scale), Math.Round(150 * scale)), shown);
        var back = LineLayout.StoredSize(shown, scale);
        Assert.InRange(back.Width, 200 - 0.5 / scale, 200 + 0.5 / scale);
        Assert.InRange(back.Height, 150 - 0.5 / scale, 150 + 0.5 / scale);
    }
}
