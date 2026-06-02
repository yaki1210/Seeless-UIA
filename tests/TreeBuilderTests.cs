using System.Diagnostics;
using System.Windows.Automation;
using SeelessUIA.Snapshot;
using Xunit;

namespace SeelessUIA.Tests;

public class TreeBuilderTests : IDisposable
{
    private Process? _notepadProcess;

    public void Dispose()
    {
        try { _notepadProcess?.Kill(); } catch { }
        _notepadProcess?.Dispose();
    }
    private static AutomationElement? FindNotepadWindow()
    {
        var root = AutomationElement.RootElement;
        var condition = new PropertyCondition(AutomationElement.ClassNameProperty, "Notepad");
        var condition2 = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
        var andCondition = new AndCondition(condition, condition2);
        return root.FindFirst(TreeScope.Children, andCondition);
    }

    private AutomationElement LaunchAndGetNotepadWindow()
    {
        var existing = FindNotepadWindow();
        if (existing != null) return existing;

        _notepadProcess = Process.Start("notepad.exe");
        Assert.NotNull(_notepadProcess);
        _notepadProcess!.WaitForInputIdle(10000);
        Thread.Sleep(2000);

        for (int i = 0; i < 20; i++)
        {
            var w = FindNotepadWindow();
            if (w != null) return w;
            Thread.Sleep(500);
        }

        throw new TimeoutException("Notepad window did not appear");
    }

    [Fact]
    public void CleanClassName_RemovesClassPrefix()
    {
        Assert.Equal("Ui::IconButton", TreeBuilder.CleanClassName("class Ui::IconButton"));
    }

    [Fact]
    public void CleanClassName_PreservesNormalClass()
    {
        Assert.Equal("Button", TreeBuilder.CleanClassName("Button"));
    }

    [Fact]
    public void CleanClassName_HandlesNull()
    {
        Assert.Equal("", TreeBuilder.CleanClassName(null));
    }

    [Fact]
    public void CleanClassName_HandlesEmpty()
    {
        Assert.Equal("", TreeBuilder.CleanClassName(""));
    }

    [Fact]
    public void CleanClassName_OnlyClass_ReturnsEmpty()
    {
        Assert.Equal("", TreeBuilder.CleanClassName("class "));
    }

    [Fact]
    public void MSAA_SafeLookup_ReturnsNullOnNet10()
    {
        // MSAA property IDs 30092-30097 are null on .NET 10 managed API.
        // TreeBuilder null-guards all uses, so null is safe.
        for (int id = 30092; id <= 30097; id++)
        {
            var prop = SafeLookup(id);
            Assert.Null(prop);
        }
    }

    [Fact]
    public void SafeLookup_InvalidId_ReturnsNull()
    {
        var prop = SafeLookup(99999);
        Assert.Null(prop);
    }

    [Fact]
    public void BuildTree_Notepad_ReturnsNonEmptyTree()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder();
        var (nodes, roots) = builder.BuildTree(window);

        Assert.NotEmpty(nodes);
        Assert.NotEmpty(roots);
        Assert.True(roots.Count < nodes.Count);
    }

    [Fact]
    public void BuildTree_RawView_ProducesMoreNodes()
    {
        var window = LaunchAndGetNotepadWindow();
        var normalBuilder = new TreeBuilder();
        var rawBuilder = new TreeBuilder(rawView: true);

        var (normalNodes, _) = normalBuilder.BuildTree(window);
        var (rawNodes, _) = rawBuilder.BuildTree(window);

        Assert.True(rawNodes.Count >= normalNodes.Count,
            $"Raw ({rawNodes.Count}) should be >= normal ({normalNodes.Count})");
    }

    [Fact]
    public void BuildTree_DepthLimit_Respected()
    {
        var window = LaunchAndGetNotepadWindow();
        var deepBuilder = new TreeBuilder(maxDepth: 16);
        var shallowBuilder = new TreeBuilder(maxDepth: 2);

        var (deepNodes, _) = deepBuilder.BuildTree(window);
        var (shallowNodes, _) = shallowBuilder.BuildTree(window);

        Assert.True(shallowNodes.Count < deepNodes.Count,
            $"Shallow ({shallowNodes.Count}) should be < deep ({deepNodes.Count})");

        foreach (var node in shallowNodes)
            Assert.True(node.Depth <= 2, $"Node depth {node.Depth} exceeds limit 2");
    }

    [Fact]
    public void BuildTree_AllNodesHaveValidDepth()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder(maxDepth: 8);
        var (nodes, roots) = builder.BuildTree(window);

        foreach (var node in nodes)
        {
            Assert.True(node.Depth >= 0);
            Assert.True(node.Depth <= 8);
        }

        foreach (var rootIdx in roots)
            Assert.Equal(0, nodes[rootIdx].Depth);
    }

    [Fact]
    public void BuildTree_ParentChildConsistency()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder();
        var (nodes, _) = builder.BuildTree(window);

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            foreach (var childIdx in node.Children)
            {
                Assert.True(childIdx >= 0);
                Assert.True(childIdx < nodes.Count);
                var child = nodes[childIdx];

                Assert.NotNull(child.ParentIdx);
                Assert.Equal(i, child.ParentIdx!.Value);
                Assert.True(child.Depth == node.Depth + 1,
                    $"Child depth {child.Depth} != parent depth {node.Depth} + 1");
            }

            if (node.ParentIdx.HasValue)
            {
                var parent = nodes[node.ParentIdx.Value];
                Assert.Contains(i, parent.Children);
            }
        }
    }

    [Fact]
    public void BuildTree_Roots_HaveNoParent()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder();
        var (nodes, roots) = builder.BuildTree(window);

        foreach (var rootIdx in roots)
            Assert.Null(nodes[rootIdx].ParentIdx);
    }

    [Fact]
    public void BuildTree_AllNodesHaveRole()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder();
        var (nodes, _) = builder.BuildTree(window);

        foreach (var node in nodes)
            Assert.False(string.IsNullOrEmpty(node.Role),
                $"Node at depth {node.Depth} has empty role");
    }

    [Fact]
    public void BuildTree_StructuralElement_HasNullPatternStates()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder();
        var (nodes, _) = builder.BuildTree(window);

        foreach (var node in nodes)
        {
            if (RoleMapping.IsStructural(node.Role))
            {
                Assert.Null(node.Expanded);
                Assert.Null(node.Selected);
                Assert.Equal("", node.Checked);
                Assert.Equal("", node.Value);
            }
        }
    }

    [Fact]
    public void BuildTree_NodesHaveRuntimeId()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder();
        var (nodes, _) = builder.BuildTree(window);

        foreach (var node in nodes)
        {
            Assert.NotNull(node.RuntimeId);
            Assert.NotEmpty(node.RuntimeId);
        }
    }

    [Fact]
    public void BuildTree_MSAAFallback_DoesNotCrash()
    {
        var window = LaunchAndGetNotepadWindow();
        var builder = new TreeBuilder(rawView: true);
        var (nodes, _) = builder.BuildTree(window);

        Assert.True(nodes.Count > 0);
    }

    [Fact]
    public void CleanName_StripsPuaIconCharacters()
    {
        // PUA icon font code points at start of name
        Assert.Equal("新建文件...", TreeBuilder.CleanName("\uEA7F 新建文件..."));
        Assert.Equal("打开文件...", TreeBuilder.CleanName("\uEA94 打开文件..."));
        Assert.Equal("打开文件夹...", TreeBuilder.CleanName("\uEAF7打开文件夹..."));
        Assert.Equal("克隆 Git 仓库...", TreeBuilder.CleanName("\uEA68 克隆 Git 仓库..."));
        Assert.Equal("连接到...", TreeBuilder.CleanName("\uEB3A连接到..."));
        Assert.Equal("生成新工作区...", TreeBuilder.CleanName("\uEC4F生成新工作区..."));
        Assert.Equal("试用新的智能体窗口", TreeBuilder.CleanName("\uEC67 试用新的智能体窗口"));
    }

    [Fact]
    public void CleanName_StripsUndefinedAndNullArtifacts()
    {
        Assert.Equal("欢迎", TreeBuilder.CleanName("欢迎, undefined"));
        Assert.Equal("item", TreeBuilder.CleanName("item, null"));
        Assert.Equal("hello", TreeBuilder.CleanName("hello, undefined"));
    }

    [Fact]
    public void CleanName_PreservesNormalText()
    {
        Assert.Equal("Visual Studio Code", TreeBuilder.CleanName("Visual Studio Code"));
        Assert.Equal("新建文件...", TreeBuilder.CleanName("新建文件..."));
        Assert.Equal("文件资源管理器", TreeBuilder.CleanName("文件资源管理器"));
        Assert.Equal("", TreeBuilder.CleanName(""));
        Assert.Equal("", TreeBuilder.CleanName("\uEA7F")); // all icon → empty
    }

    [Fact]
    public void CleanName_HandlesMultiplePuaChars()
    {
        Assert.Equal("新建  文件夹", TreeBuilder.CleanName("\uEA7F新建\uEA94  \uEAF7文件夹"));
    }

    private static AutomationProperty? SafeLookup(int id)
    {
        try { return AutomationProperty.LookupById(id); }
        catch { return null; }
    }
}
