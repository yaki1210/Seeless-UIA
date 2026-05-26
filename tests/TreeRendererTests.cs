using SeelessUIA.Snapshot;
using Xunit;

namespace SeelessUIA.Tests;

public class TreeRendererTests
{
    [Fact]
    public void Render_ProducesIndentedOutput()
    {
        var nodes = new List<UiaNode>
        {
            new() { // 0: root window (should be skipped)
                Role = "window",
                ControlTypeName = "ControlType.Window",
                Children = [1],
            },
            new() { // 1: button
                Role = "button",
                Name = "OK",
                RefId = "e1",
                HasRef = true,
                HasInvokePattern = true,
                CursorKind = "clickable",
                CursorHints = new List<string>(),
            },
        };

        nodes[1].ParentIdx = 0;

        var options = new SnapshotOptions { Interactive = false };
        var renderer = new TreeRenderer(options);
        var output = renderer.Render(nodes, [0]);

        // Window should be skipped, button rendered at indent 0
        Assert.Contains("- button", output);
        Assert.Contains("OK", output);
        Assert.Contains("ref=e1", output);
        Assert.Contains("clickable", output);
        Assert.Contains("clickable", output);
    }

    [Fact]
    public void Render_SkipsClearedNodesButRendersChildren()
    {
        var nodes = new List<UiaNode>
        {
            new() { // 0: pane (cleared)
                IsCleared = true,
                Children = [1],
            },
            new() { // 1: button inside cleared pane
                Role = "button",
                Name = "Submit",
                RefId = "e1",
                HasRef = true,
            },
        };

        nodes[1].ParentIdx = 0;

        var options = new SnapshotOptions();
        var renderer = new TreeRenderer(options);
        var output = renderer.Render(nodes, [0]);

        Assert.Contains("button", output);
        Assert.Contains("Submit", output);
    }

    [Fact]
    public void Render_InteractiveModeFiltersNonRefNodes()
    {
        var nodes = new List<UiaNode>
        {
            new() {
                Role = "group",
                Children = [1, 2],
            },
            new() { // 1: non-ref text (should be filtered)
                Role = "text",
                Name = "Some label",
            },
            new() { // 2: ref button (should appear)
                Role = "button",
                Name = "Click Me",
                RefId = "e1",
                HasRef = true,
            },
        };

        nodes[1].ParentIdx = 0;
        nodes[2].ParentIdx = 0;

        var options = new SnapshotOptions { Interactive = true };
        var renderer = new TreeRenderer(options);
        var output = renderer.Render(nodes, [0]);

        Assert.DoesNotContain("Some label", output);
        Assert.Contains("Click Me", output);
        Assert.Contains("e1", output);
    }

    [Fact]
    public void Render_ShowsEmptyMessageWhenNoContent()
    {
        var nodes = new List<UiaNode>();
        var options = new SnapshotOptions { Interactive = true };
        var renderer = new TreeRenderer(options);
        var output = renderer.Render(nodes, []);

        Assert.Contains("no interactive elements", output);
    }

    [Fact]
    public void Render_ShowsDisabledAttribute()
    {
        var nodes = new List<UiaNode>
        {
            new() {
                Role = "button",
                Name = "Disabled Button",
                RefId = "e1",
                HasRef = true,
                IsEnabled = false,
            },
        };

        var options = new SnapshotOptions();
        var renderer = new TreeRenderer(options);
        var output = renderer.Render(nodes, [0]);

        Assert.Contains("disabled", output);
    }

    [Fact]
    public void Render_ShowsValue()
    {
        var nodes = new List<UiaNode>
        {
            new() {
                Role = "textbox",
                Name = "Username",
                Value = "admin",
                RefId = "e1",
                HasRef = true,
            },
        };

        var options = new SnapshotOptions();
        var renderer = new TreeRenderer(options);
        var output = renderer.Render(nodes, [0]);

        Assert.Contains(": admin", output);
    }
}
