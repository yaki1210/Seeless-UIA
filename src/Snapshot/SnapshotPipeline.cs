using System.Diagnostics;
using System.Windows.Automation;
using SeelessUIA.Element;

namespace SeelessUIA.Snapshot;

public class SnapshotPipeline
{
    private readonly TreeBuilder _treeBuilder;
    private readonly TreeCleaner _treeCleaner;
    private readonly SnapshotOptions _options;
    private readonly RefMap _refMap;
    private readonly RoleNameTracker _roleNameTracker = new();

    public SnapshotPipeline(SnapshotOptions options, RefMap refMap)
    {
        _options = options;
        _refMap = refMap;
        _treeBuilder = new TreeBuilder(options.Depth ?? 16, options.RawView);
        _treeCleaner = new TreeCleaner();
    }

    public string TakeSnapshot(AutomationElement rootElement)
    {
        // Stage 1: Build tree (UIA COM calls dominate)
        var sw = Stopwatch.StartNew();
        var (nodes, rootIndices) = _treeBuilder.BuildTree(rootElement);
        long tBuild = sw.ElapsedMilliseconds;

        long tClean = 0;
        if (!_options.NoClean)
        {
            // Stage 2: Clean the tree (in-memory)
            sw.Restart();
            _treeCleaner.Clean(nodes, _options.Interactive);
            tClean = sw.ElapsedMilliseconds;
        }

        // Stage 3: Detect interactivity (in-memory)
        sw.Restart();
        DetectInteractivity(nodes);
        long tDetect = sw.ElapsedMilliseconds;

        // Stage 4: Assign ref IDs (in-memory)
        sw.Restart();
        AssignRefs(nodes);
        long tRefs = sw.ElapsedMilliseconds;

        // Stage 5: Render to text (in-memory)
        sw.Restart();
        var renderer = new TreeRenderer(_options);
        var result = renderer.Render(nodes, rootIndices);
        long tRender = sw.ElapsedMilliseconds;

        Console.Error.WriteLine(
            $"[perf] build:{tBuild}ms clean:{tClean}ms detect:{tDetect}ms refs:{tRefs}ms render:{tRender}ms nodes:{nodes.Count}");

        return result;
    }

    private static void DetectInteractivity(List<UiaNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsCleared) continue;
            var (kind, hints) = ClassifyInteractivity(node);
            if (!string.IsNullOrEmpty(kind))
            {
                node.CursorKind = kind;
                node.CursorHints = hints;
            }
        }
    }

    /// <summary>
    /// Classify a node's interactivity based on its available UIA Patterns.
    /// Hints only include non-obvious information (state, not redundant pattern name).
    /// </summary>
    public static (string kind, List<string> hints) ClassifyInteractivity(UiaNode node)
    {
        if (node.HasInvokePattern)
            return ("clickable", []);

        if (node.HasTogglePattern)
        {
            string state = node.Checked switch
            {
                "true" => "on",
                "mixed" => "mixed",
                _ => "off"
            };
            return ("toggleable", [state]);
        }

        if (node.HasValuePattern && node.Role is "textbox" or "combobox" or "edit")
            return ("editable", []);

        if (node.HasSelectionItemPattern)
            return ("selectable", []);

        if (node.HasExpandCollapsePattern)
        {
            string state = node.Expanded == true ? "expanded" : "collapsed";
            return ("expandable", [state]);
        }

        if (node.HasScrollPattern)
            return ("scrollable", []);

        if (node.IsKeyboardFocusable)
            return ("focusable", []);

        return ("", []);
    }

    private void AssignRefs(List<UiaNode> nodes)
    {
        var nextRef = _refMap.NextRefNum();

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsCleared) continue;

            // Skip Window-type elements (never rendered — transparent root)
            // Also skip chrome elements (TitleBar, MenuBar, ToolTip)
            if (RoleMapping.ShouldSkip(ControlTypeLookup.GetId(node.ControlTypeName))
                || RoleMapping.ShouldSkipChildren(ControlTypeLookup.GetId(node.ControlTypeName)))
                continue;

            bool shouldRef = false;
            if (RoleMapping.IsInteractive(node.Role))
                shouldRef = true;
            else if (RoleMapping.IsContent(node.Role) && !string.IsNullOrEmpty(node.Name))
                shouldRef = true;
            else if (!string.IsNullOrEmpty(node.CursorKind))
                shouldRef = true;

            if (shouldRef && node.Role == "generic" && string.IsNullOrEmpty(node.Name))
                shouldRef = false;

            if (shouldRef)
            {
                int nth = _roleNameTracker.Track(node.Role, node.Name);
                var dupes = _roleNameTracker.GetDuplicates();
                var key = $"{node.Role}:{node.Name}";
                int? actualNth = dupes.ContainsKey(key) ? nth : null;
                var refId = $"e{nextRef}";
                nextRef++;
                _refMap.Add(refId, node.RuntimeId, node.Role, node.Name, actualNth);
                node.HasRef = true;
                node.RefId = refId;
            }
        }

        _refMap.SetNextRefNum(nextRef);
    }
}
