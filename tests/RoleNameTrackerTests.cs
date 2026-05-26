using SeelessUIA.Element;
using Xunit;

namespace SeelessUIA.Tests;

public class RoleNameTrackerTests
{
    [Fact]
    public void Track_ReturnsSequentialIndices()
    {
        var tracker = new RoleNameTracker();

        Assert.Equal(0, tracker.Track("button", "Submit"));
        Assert.Equal(1, tracker.Track("button", "Submit"));
        Assert.Equal(0, tracker.Track("button", "Cancel"));
        Assert.Equal(2, tracker.Track("button", "Submit"));
    }

    [Fact]
    public void GetDuplicates_ReturnsOnlyMultis()
    {
        var tracker = new RoleNameTracker();

        tracker.Track("button", "Submit");  // 0
        tracker.Track("button", "Submit");  // 1
        tracker.Track("button", "Cancel");  // 0
        tracker.Track("textbox", "Name");   // 0

        var dups = tracker.GetDuplicates();

        Assert.True(dups.ContainsKey("button:Submit"));
        Assert.Equal(2, dups["button:Submit"]);
        Assert.False(dups.ContainsKey("button:Cancel"));
        Assert.False(dups.ContainsKey("textbox:Name"));
    }
}
