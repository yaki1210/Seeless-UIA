using System.Windows;
using SeelessUIA.Snapshot;
using Xunit;

namespace SeelessUIA.Tests;

public class TreeCleanerTests
{
    [Fact]
    public void FilterBasic_ClearsZeroSizedNodes()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", Name = "", BoundingRect = new Rect(0, 0, 0, 0) },
            new() { Role = "button", Name = "Cancel", BoundingRect = new Rect(10, 10, 100, 30) },
        };
        new TreeCleaner().Clean(nodes, false);
        Assert.True(nodes[0].IsCleared, "Anonymous zero-sized element should be cleared");
        Assert.False(nodes[1].IsCleared);
    }

    [Fact]
    public void FilterBasic_KeepsZeroSizedNamedElement()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", Name = "Notifications (alt+T)", BoundingRect = new Rect(0, 0, 0, 0) },
        };
        new TreeCleaner().Clean(nodes, false);
        Assert.False(nodes[0].IsCleared, "Named zero-sized element should be kept");
    }

    [Fact]
    public void FilterBasic_ClearsOffscreenNodes()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", Name = "", IsOffscreen = true, BoundingRect = new Rect(0, 0, 100, 30) },
            new() { Role = "button", Name = "Visible", BoundingRect = new Rect(10, 10, 100, 30) },
        };
        new TreeCleaner().Clean(nodes, false);
        Assert.True(nodes[0].IsCleared, "Anonymous offscreen node should be cleared");
        Assert.False(nodes[1].IsCleared);
    }

    [Fact]
    public void FilterBasic_KeepsOffscreenNamedNode()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", Name = "Notifications (alt+T)", AutomationId = "toast-region", IsOffscreen = true, BoundingRect = new Rect(0, 0, 100, 30) },
        };
        new TreeCleaner().Clean(nodes, false);
        Assert.False(nodes[0].IsCleared, "Named offscreen node should be kept");
    }

    [Fact]
    public void DeduplicateName_ClearsChildWhenSameName()
    {
        var nodes = new List<UiaNode>
        {
            new() { // 0: parent
                Role = "button", Name = "Submit",
                Children = [1],
                BoundingRect = new Rect(0, 0, 100, 30),
            },
            new() { // 1: child text
                Role = "text", Name = "Submit",
                BoundingRect = new Rect(5, 5, 90, 20),
            },
        };

        nodes[1].ParentIdx = 0;

        new TreeCleaner().Clean(nodes, false);

        Assert.False(nodes[0].IsCleared);
        Assert.True(nodes[1].IsCleared);
    }

    [Fact]
    public void DeduplicateName_KeepsChildWhenDifferentName()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "button", Name = "Click Me", Children = [1], BoundingRect = new Rect(0, 0, 100, 30) },
            new() { Role = "text", Name = "Different", BoundingRect = new Rect(5, 5, 90, 20) },
        };

        nodes[1].ParentIdx = 0;

        new TreeCleaner().Clean(nodes, false);

        Assert.False(nodes[0].IsCleared);
        Assert.False(nodes[1].IsCleared);
    }

    [Fact]
    public void CollapseContainers_ClearsSingleChildPane()
    {
        var nodes = new List<UiaNode>
        {
            new() { // 0: pane with empty name and 1 child
                Role = "generic",
                ControlTypeName = "ControlType.Pane",
                Children = [1],
                BoundingRect = new Rect(0, 0, 100, 100),
            },
            new() { // 1: button inside pane
                Role = "button", Name = "OK",
                BoundingRect = new Rect(10, 10, 80, 30),
            },
        };

        nodes[1].ParentIdx = 0;

        new TreeCleaner().Clean(nodes, false);

        Assert.True(nodes[0].IsCleared);  // Pane should be collapsed
        Assert.False(nodes[1].IsCleared);
    }

    [Fact]
    public void RelabelContainersByClass_MapsFrameworkClassesToSemanticRoles()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", ClassName = "QMenuBar", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "generic", ClassName = "MyCoolToolBar", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "generic", ClassName = "QStatusBar", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "generic", ClassName = "QTabBar", BoundingRect = new Rect(0,0,10,10) },
        };

        new TreeCleaner().Clean(nodes, false);

        Assert.Equal("menu", nodes[0].Role);
        Assert.Equal("toolbar", nodes[1].Role);
        Assert.Equal("statusbar", nodes[2].Role);
        Assert.Equal("tablist", nodes[3].Role);
    }

    [Fact]
    public void RelabelContainersByClass_SysTabControl32_MapsToTablist()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", ClassName = "SysTabControl32", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "generic", ClassName = "WindowsForms10.SysTabControl32.app.0.378734a", BoundingRect = new Rect(0,0,10,10) },
        };

        new TreeCleaner().Clean(nodes, false);

        Assert.Equal("tablist", nodes[0].Role);
        Assert.Equal("tablist", nodes[1].Role);
    }

    [Fact]
    public void RelabelContainersByClass_WpfMenu_MapsToMenu()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", ClassName = "Menu", FrameworkId = "WPF", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "generic", ClassName = "ContextMenu", FrameworkId = "WPF", BoundingRect = new Rect(0,0,10,10) },
        };

        new TreeCleaner().Clean(nodes, false);

        Assert.Equal("menu", nodes[0].Role);
        Assert.Equal("menu", nodes[1].Role);
    }

    [Fact]
    public void RelabelContainersByClass_WpfTabControl_MapsToTablist()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", ClassName = "TabControl", FrameworkId = "WPF", BoundingRect = new Rect(0,0,10,10) },
        };

        new TreeCleaner().Clean(nodes, false);

        Assert.Equal("tablist", nodes[0].Role);
    }

    [Fact]
    public void RelabelContainersByClass_WpfClassWithoutFrameworkId_NotRelabeled()
    {
        var nodes = new List<UiaNode>
        {
            new() { Role = "generic", ClassName = "Menu", BoundingRect = new Rect(0,0,10,10) },
        };

        new TreeCleaner().Clean(nodes, false);

        Assert.Equal("generic", nodes[0].Role);
    }

    [Fact]
    public void RelabelContainersHeuristically_MapsChildrenDistributionToSemanticRoles()
    {
        var nodes = new List<UiaNode>
        {
            // Case 1: menu items under generic container
            new() { Role = "generic", Children = [1, 2], BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "menuitem", Name = "Menu1", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "menuitem", Name = "Menu2", BoundingRect = new Rect(0,0,10,10) },

            // Case 2: tabs under generic container
            new() { Role = "generic", Children = [4, 5], BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "tab", Name = "Tab1", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "tab", Name = "Tab2", BoundingRect = new Rect(0,0,10,10) },

            // Case 3: listitems under generic container
            new() { Role = "generic", Children = [7, 8], BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "listitem", Name = "Item1", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "listitem", Name = "Item2", BoundingRect = new Rect(0,0,10,10) },

            // Case 4: buttons under generic container (toolbar)
            new() { Role = "generic", Children = [10, 11, 12], BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "button", Name = "Btn1", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "separator", Name = "Sep1", BoundingRect = new Rect(0,0,10,10) },
            new() { Role = "button", Name = "Btn2", BoundingRect = new Rect(0,0,10,10) },
        };

        // link parent indexes for children
        nodes[1].ParentIdx = 0; nodes[2].ParentIdx = 0;
        nodes[4].ParentIdx = 3; nodes[5].ParentIdx = 3;
        nodes[7].ParentIdx = 6; nodes[8].ParentIdx = 6;
        nodes[10].ParentIdx = 9; nodes[11].ParentIdx = 9; nodes[12].ParentIdx = 9;

        new TreeCleaner().Clean(nodes, false);

        Assert.Equal("menu", nodes[0].Role);
        Assert.Equal("tablist", nodes[3].Role);
        Assert.Equal("list", nodes[6].Role);
        Assert.Equal("toolbar", nodes[9].Role);
    }
}
