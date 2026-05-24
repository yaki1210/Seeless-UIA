using SeelessUIA.Element;
using Xunit;

namespace SeelessUIA.Tests;

public class RefMapTests
{
    [Fact]
    public void AssignNextRef_ReturnsSequentialIds()
    {
        var map = new RefMap();

        var r1 = map.AssignNextRef([1, 2], "button", "OK", null);
        var r2 = map.AssignNextRef([3, 4], "button", "Cancel", null);
        var r3 = map.AssignNextRef([5, 6], "textbox", "Name", 0);

        Assert.Equal("e1", r1);
        Assert.Equal("e2", r2);
        Assert.Equal("e3", r3);
    }

    [Fact]
    public void Get_ReturnsCorrectEntry()
    {
        var map = new RefMap();
        map.Add("e42", [10, 20], "button", "Submit", null);

        var entry = map.Get("e42");

        Assert.NotNull(entry);
        Assert.Equal("button", entry!.Role);
        Assert.Equal("Submit", entry.Name);
        Assert.Null(entry.Nth);
        Assert.Equal([10, 20], entry.RuntimeId);
    }

    [Fact]
    public void Get_ReturnsNullForMissing()
    {
        var map = new RefMap();
        Assert.Null(map.Get("e999"));
    }

    [Fact]
    public void EntriesSorted_ReturnsSortedByRefNumber()
    {
        var map = new RefMap();
        map.Add("e3", [1], "button", "C", null);
        map.Add("e1", [2], "button", "A", null);
        map.Add("e2", [3], "textbox", "B", 0);

        var sorted = map.EntriesSorted();

        Assert.Equal(3, sorted.Count);
        Assert.Equal("e1", sorted[0].Key);
        Assert.Equal("e2", sorted[1].Key);
        Assert.Equal("e3", sorted[2].Key);
    }

    [Fact]
    public void Clear_EmptiesMap()
    {
        var map = new RefMap();
        map.Add("e1", [1], "button", "X", null);
        map.Clear();

        Assert.Null(map.Get("e1"));
    }
}
