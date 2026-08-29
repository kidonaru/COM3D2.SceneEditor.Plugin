using COM3D2.SceneEditor.Plugin;
using Xunit;

public class TabGroupConfigEntryTests
{
    [Fact]
    public void ParsesValidEntry()
    {
        Assert.True(TabGroupConfigEntry.TryParse("10:10,20,30", out var e));
        Assert.Equal(10, e.activeId);
        Assert.Equal(new[] { 10, 20, 30 }, e.memberIds);
    }

    [Fact]
    public void SkipsInvalidMemberIds()
    {
        Assert.True(TabGroupConfigEntry.TryParse("10:10,abc,30", out var e));
        Assert.Equal(new[] { 10, 30 }, e.memberIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10")]
    [InlineData("abc:1,2")]
    [InlineData("1:2:3")]
    public void RejectsMalformedEntries(string entry)
    {
        Assert.False(TabGroupConfigEntry.TryParse(entry, out _));
    }
}
