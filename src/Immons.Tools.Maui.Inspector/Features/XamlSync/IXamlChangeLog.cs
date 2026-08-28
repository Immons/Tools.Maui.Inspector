using Immons.Tools.Maui.Inspector.Features.Structure;

namespace Immons.Tools.Maui.Inspector.Features.XamlSync;

/// <summary>Registry of applied edits destined for the sync tool.</summary>
internal interface IXamlChangeLog
{
    /// <summary>Runtime switch (toggled from the web client). Off by default — edits stay in-memory only.</summary>
    bool Enabled { get; set; }

    /// <summary>Records an applied edit; no-op when disabled or the object has no XAML source info.</summary>
    void Record(object target, string attribute, string value);

    /// <summary>Upserts the insert snippet for an inspector-added element (latest wins per op).</summary>
    void RecordInsert(StructureOp op);

    /// <summary>
    /// Re-serializes and upserts the insert snippet of the pasted/added subtree containing the
    /// element (the element itself or an ancestor). No-op when no such subtree exists — call it
    /// after any structural change so edits INSIDE pasted content reach the file too.
    /// </summary>
    void RefreshSubtreeSnippet(VisualElement element);

    /// <summary>Asks the tool to take a previously sent insert back out of the file.</summary>
    void CancelInsert(StructureOp op);

    /// <summary>Asks the tool to delete an element that exists in the XAML source.</summary>
    void RecordElementRemove(StructureOp op);

    /// <summary>Undoes <see cref="RecordElementRemove"/> — the tool restores the exact removed text.</summary>
    void RestoreElement(StructureOp op);

    /// <summary>
    /// Edits one setter of a style resource. Anchored by the style's key/TargetType inside the
    /// dictionary's source file — setters carry no line information of their own.
    /// </summary>
    void RecordStyleSetter(string? dictionarySource, string styleKey, string targetType, string property, string value);

    /// <summary>New value of a scalar/color resource, keyed by dictionary file + x:Key.</summary>
    void RecordResourceValue(string? dictionarySource, string key, string value);

    /// <summary>Seq of the most recently recorded change — 0 when nothing was recorded yet.</summary>
    long LastSeq { get; }

    /// <summary>The updater's verdict on one applied change.</summary>
    void AckWrite(long seq, bool ok, string message);

    /// <summary>"pending" | "applied" | "failed" (+ message) for a recorded change.</summary>
    (string State, string? Message) WriteStatus(long seq);

    /// <summary>Inserts a Style block into the page's resources (upsert per op; cancel on undo).</summary>
    void RecordStyleResource(StructureOp op);

    /// <summary>Undoes <see cref="RecordStyleResource"/> — the tool takes the style block back out.</summary>
    void CancelStyleResource(StructureOp op);

    /// <summary>Moves an element's XAML span before/after the sibling it jumped over.</summary>
    void RecordElementMove(StructureOp op);

    /// <summary>Moves an element's XAML span into another parent (same file only).</summary>
    void RecordElementReparent(StructureOp op);

    /// <summary>Wraps an element's XAML span in a new container element (upsert per op).</summary>
    void RecordWrap(StructureOp op);

    /// <summary>Undoes <see cref="RecordWrap"/> — the tool strips the wrapper tags back off.</summary>
    void CancelWrap(StructureOp op);

    /// <summary>Strips a source-backed container's tags, keeping (and re-indenting) its children.</summary>
    void RecordElementUnwrap(StructureOp op);

    /// <summary>Undoes <see cref="RecordElementUnwrap"/> — the tool puts the container's tags back.</summary>
    void RestoreElementUnwrap(StructureOp op);

    /// <summary>
    /// includeStructural guards against version skew: a sync tool that predates element
    /// operations would apply them through its attribute path and corrupt the file, so
    /// structural changes are only served to clients that declare support (caps=el).
    /// </summary>
    string ToJson(long since, bool includeStructural);
}
