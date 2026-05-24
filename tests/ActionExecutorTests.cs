using SeelessUIA.Interaction;
using Xunit;

namespace SeelessUIA.Tests;

public class ActionExecutorTests
{
    [Theory]
    [InlineData("Control+a", "a", 2)]
    [InlineData("Shift+Enter", "Enter", 8)]
    [InlineData("Control+Shift+a", "a", 10)]
    [InlineData("Meta+c", "c", 4)]
    [InlineData("Alt+F4", "F4", 1)]
    [InlineData("Ctrl+Tab", "Tab", 2)]
    public void ParseKeyChord_ParsesCorrectly(string input, string expectedKey, int expectedModifiers)
    {
        var (key, modifiers) = ActionExecutor.ParseKeyChord(input);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedModifiers, modifiers);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("a")]
    [InlineData("Tab")]
    [InlineData("F1")]
    public void ParseKeyChord_NoModifiers_ReturnsInputUnchanged(string input)
    {
        var (key, modifiers) = ActionExecutor.ParseKeyChord(input);
        Assert.Equal(input, key);
        Assert.Equal(0, modifiers);
    }

    [Theory]
    [InlineData("+")]
    public void ParseKeyChord_PlusIsNotModifier(string input)
    {
        var (key, modifiers) = ActionExecutor.ParseKeyChord(input);
        Assert.Equal(input, key);
        Assert.Equal(0, modifiers);
    }

    [Theory]
    [InlineData("ENTER", 0x0D)]
    [InlineData("esc", 0x1B)]
    [InlineData("SPACE", 0x20)]
    [InlineData("backspace", 0x08)]
    [InlineData("DEL", 0x2E)]
    [InlineData("UP", 0x26)]
    [InlineData("DOWN", 0x28)]
    [InlineData("LEFT", 0x25)]
    [InlineData("RIGHT", 0x27)]
    [InlineData("HOME", 0x24)]
    [InlineData("END", 0x23)]
    [InlineData("PAGEUP", 0x21)]
    [InlineData("PAGEDOWN", 0x22)]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("ctrl", 0x11)]
    [InlineData("shift", 0x10)]
    [InlineData("win", 0x5B)]
    public void KeyNameToVk_MapsCorrectly(string key, short expectedVk)
    {
        var vk = ActionExecutor.KeyNameToVk(key);
        Assert.Equal(expectedVk, vk);
    }

    [Fact]
    public void KeyNameToVk_UnknownKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => ActionExecutor.KeyNameToVk("GARBAGE"));
    }
}
