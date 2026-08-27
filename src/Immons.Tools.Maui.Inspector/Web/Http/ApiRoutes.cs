namespace Immons.Tools.Maui.Inspector.Web.Http;

/// <summary>All routes served by the embedded server — the single source of truth for paths.</summary>
internal static class ApiRoutes
{
    public static class Assets
    {
        public const string Root = "/";
        public const string Css = "/app.css";
        public const string JsPrefix = "/js/";
        public const string JsSuffix = ".js";
    }

    public static class Tree
    {
        public const string List = "/api/tree";
    }

    public static class Dump
    {
        public const string Text = "/api/dump";
    }

    public static class Selection
    {
        public const string State = "/api/selection";
    }

    public static class Toggles
    {
        public const string MeasureMode = "/api/measure-mode";
        public const string SelectMode = "/api/select-mode";
        public const string Overlay = "/api/overlay";
        public const string DebugPaint = "/api/debug-paint";
        public const string Perf = "/api/perf";
        public const string SlowAnimations = "/api/slow-animations";
        public const string Wysiwyg = "/api/wysiwyg";
    }

    public static class Elements
    {
        public const string Prefix = "/api/element/";
        public const string SelectVerb = "select";
        public const string PropertyVerb = "property";
        public const string ActionVerb = "action";
        public const string StructureVerb = "structure";
    }

    public static class Structure
    {
        public const string Catalog = "/api/structure/catalog";
        public const string AddAt = "/api/structure/add-at";
        public const string DropTarget = "/api/structure/drop-target";
        public const string Hit = "/api/structure/hit";
        public const string GridInfo = "/api/structure/grid-info";
    }

    public static class Resources
    {
        public const string List = "/api/resources";
        public const string Set = "/api/resources/set";
        public const string SetSetter = "/api/resources/set-setter";
    }

    public static class History
    {
        public const string List = "/api/history";
        public const string Undo = "/api/history/undo";
        public const string Redo = "/api/history/redo";
    }

    public static class Network
    {
        public const string List = "/api/network";
        public const string Body = "/api/network/body";
        public const string Clear = "/api/network/clear";
    }

    public static class MockRules
    {
        public const string List = "/api/mock/rules";
        public const string Save = "/api/mock/rules/save";
        public const string Delete = "/api/mock/rules/delete";
        public const string Enable = "/api/mock/rules/enable";
        public const string Import = "/api/mock/rules/import";
        public const string Mocking = "/api/mock/rules/mocking";
        public const string Scenario = "/api/mock/rules/scenario";
        public const string ScenarioAdd = "/api/mock/rules/scenario/add";
        public const string ScenarioRemove = "/api/mock/rules/scenario/remove";
        public const string RecordStart = "/api/mock/record/start";
        public const string RecordStop = "/api/mock/record/stop";
        public const string RecordCancel = "/api/mock/record/cancel";
    }

    public static class Intercept
    {
        public const string State = "/api/intercept";
        public const string Prefix = "/api/intercept/";
        public const string Config = "/api/intercept/config";
        public const string Resume = "/api/intercept/resume";
        public const string Abort = "/api/intercept/abort";
    }

    public static class Logs
    {
        public const string List = "/api/logs";
    }

    public static class Changes
    {
        public const string List = "/api/changes";
        public const string Ack = "/api/changes/ack";
        public const string Status = "/api/changes/status";
    }

    public static class Mirror
    {
        public const string Screenshot = "/api/screenshot";
        public const string SelectAt = "/api/select-at";
        public const string Tap = "/api/tap";
        public const string Key = "/api/key";
    }

    public static class Broadcast
    {
        public const string Ping = "/api/ping";
        public const string Property = "/api/broadcast/property";
        public const string Action = "/api/broadcast/action";
    }

    public static class Cookbook
    {
        public const string Catalog = "/api/cookbook";
        public const string Open = "/api/cookbook/open";
        public const string Preview = "/api/cookbook/preview";
        public const string State = "/api/cookbook/state";
        public const string Focus = "/api/cookbook/focus";
    }

    public static class Theme
    {
        public const string State = "/api/theme";
    }

    public static class Measure
    {
        public const string Compute = "/api/measure";
        public const string Clear = "/api/clear";
    }

    public static class Memory
    {
        public const string Stats = "/api/memory";
        public const string Gc = "/api/memory/gc";
        public const string Peers = "/api/memory/peers";
        public const string Snapshot = "/api/memory/snapshot";
        public const string Baseline = "/api/memory/baseline";
        public const string Dumps = "/api/memory/dumps";
        public const string DumpReport = "/api/memory/dump/report";
        public const string HeapDump = "/api/memory/heapdump";
        public const string DumpRequest = "/api/memory/dump/request";
        public const string DumpCancel = "/api/memory/dump/cancel";
        public const string DumpPending = "/api/memory/dump/pending";
        public const string DumpStatus = "/api/memory/dump/status";
        public const string DumpResult = "/api/memory/dump/result";
        public const string DumpTrace = "/api/memory/dump/trace";
        public const string AllocRequest = "/api/memory/alloc/request";
        public const string Settings = "/api/memory/settings";
        public const string Ledger = "/api/memory/ledger";
        public const string Snapshots = "/api/memory/snapshots";
        public const string Images = "/api/memory/images";
    }
}
