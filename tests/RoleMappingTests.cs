using SeelessUIA.Snapshot;
using Xunit;

namespace SeelessUIA.Tests;

public class RoleMappingTests
{
    [Fact]
    public void GetRole_MapsButtonCorrectly()
    {
        var role = RoleMapping.GetRole(System.Windows.Automation.ControlType.Button.Id);
        Assert.Equal("button", role);
    }

    [Fact]
    public void GetRole_MapsEditCorrectly()
    {
        var role = RoleMapping.GetRole(System.Windows.Automation.ControlType.Edit.Id);
        Assert.Equal("textbox", role);
    }

    [Fact]
    public void IsInteractive_ReturnsTrueForClickableRoles()
    {
        Assert.True(RoleMapping.IsInteractive("button"));
        Assert.True(RoleMapping.IsInteractive("textbox"));
        Assert.True(RoleMapping.IsInteractive("checkbox"));
        Assert.True(RoleMapping.IsInteractive("radio"));
    }

    [Fact]
    public void IsInteractive_ReturnsFalseForStructuralRoles()
    {
        Assert.False(RoleMapping.IsInteractive("generic"));
        Assert.False(RoleMapping.IsInteractive("pane"));
        Assert.False(RoleMapping.IsInteractive("group"));
    }

    [Fact]
    public void IsContent_ReturnsTrueForContentRoles()
    {
        Assert.True(RoleMapping.IsContent("heading"));
        Assert.True(RoleMapping.IsContent("text"));
        Assert.True(RoleMapping.IsContent("image"));
    }

    [Fact]
    public void IsContent_ReturnsFalseForInteractiveRoles()
    {
        Assert.False(RoleMapping.IsContent("button"));
        Assert.False(RoleMapping.IsContent("textbox"));
    }

    [Fact]
    public void IsTransparent_ReturnsTrueForPaneGroup()
    {
        Assert.True(RoleMapping.IsTransparent(
            System.Windows.Automation.ControlType.Pane.Id));
        Assert.True(RoleMapping.IsTransparent(
            System.Windows.Automation.ControlType.Group.Id));
    }

    [Fact]
    public void IsTransparent_ReturnsFalseForButton()
    {
        Assert.False(RoleMapping.IsTransparent(
            System.Windows.Automation.ControlType.Button.Id));
    }
}
