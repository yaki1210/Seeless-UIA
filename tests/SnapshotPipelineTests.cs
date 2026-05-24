using SeelessUIA.Snapshot;
using Xunit;

namespace SeelessUIA.Tests;

public class SnapshotPipelineTests
{
    [Fact]
    public void ClassifyInteractivity_InvokePattern_ReturnsClickable()
    {
        var node = new UiaNode { Role = "button", HasInvokePattern = true };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("clickable", kind);
        Assert.Empty(hints);  // [invoke] removed as redundant
    }

    [Fact]
    public void ClassifyInteractivity_TogglePattern_ReturnsToggleable()
    {
        var node = new UiaNode { Role = "checkbox", HasTogglePattern = true, Checked = "true" };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("toggleable", kind);
        Assert.Contains("on", hints);
        Assert.DoesNotContain("toggle", hints);  // redundant
        Assert.Single(hints);
    }

    [Fact]
    public void ClassifyInteractivity_ValuePattern_ReturnsEditable()
    {
        var node = new UiaNode { Role = "textbox", HasValuePattern = true };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("editable", kind);
        Assert.Empty(hints);
    }

    [Fact]
    public void ClassifyInteractivity_SelectionItemPattern_ReturnsSelectable()
    {
        var node = new UiaNode { Role = "listitem", HasSelectionItemPattern = true };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("selectable", kind);
        Assert.Empty(hints);
    }

    [Fact]
    public void ClassifyInteractivity_KeyboardFocusable_ReturnsFocusable()
    {
        var node = new UiaNode { Role = "custom", IsKeyboardFocusable = true };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("focusable", kind);
        Assert.Empty(hints);
    }

    [Fact]
    public void ClassifyInteractivity_ExpandCollapse_ReturnsExpandableWithState()
    {
        var node = new UiaNode { Role = "menuitem", HasExpandCollapsePattern = true, Expanded = true };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("expandable", kind);
        Assert.Contains("expanded", hints);
        Assert.DoesNotContain("expand", hints);
        Assert.Single(hints);
    }

    [Fact]
    public void ClassifyInteractivity_NoPatterns_ReturnsEmpty()
    {
        var node = new UiaNode { Role = "text" };
        var (kind, hints) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("", kind);
        Assert.Empty(hints);
    }

    [Fact]
    public void ClassifyInteractivity_InvokeTakesPriority()
    {
        var node = new UiaNode { Role = "button", HasInvokePattern = true, HasTogglePattern = true, HasValuePattern = true };
        var (kind, _) = SnapshotPipeline.ClassifyInteractivity(node);
        Assert.Equal("clickable", kind);
    }
}
