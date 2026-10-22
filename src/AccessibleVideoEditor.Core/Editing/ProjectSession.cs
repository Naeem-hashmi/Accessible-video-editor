using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// The open project with its file name, unsaved-changes flag and undo history. Every change goes through
/// <see cref="Execute"/>, which takes an undo snapshot first and rolls back automatically if the edit fails
/// or throws, so the project can never be left half-changed.
/// </summary>
public sealed class ProjectSession
{
    public const int MaxUndo = 200;

    private readonly LinkedList<(string Label, string Json)> _undo = new();
    private readonly Stack<(string Label, string Json)> _redo = new();

    public ProjectSession(Project project, string? filePath = null)
    {
        Project = project;
        FilePath = filePath;
    }

    public Project Project { get; private set; }

    public string? FilePath { get; set; }

    public bool IsDirty { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Last?.Value.Label;

    public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

    /// <summary>Raised after any change, undo or redo.</summary>
    public event EventHandler? Changed;

    public EditResult Execute(string label, Func<Project, EditResult> edit)
    {
        var snapshot = ProjectSerializer.ToJson(Project, indented: false);
        EditResult result;
        try
        {
            result = edit(Project);
        }
        catch (Exception ex)
        {
            Project = ProjectSerializer.FromJson(snapshot);
            Changed?.Invoke(this, EventArgs.Empty);
            return EditResult.Fail($"{label} failed: {ex.Message}");
        }

        if (!result.Ok)
        {
            // Failed edits must not leave partial changes behind.
            Project = ProjectSerializer.FromJson(snapshot);
            return result;
        }

        _undo.AddLast((label, snapshot));
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public EditResult Undo()
    {
        if (_undo.Last is not { } node)
        {
            return EditResult.Fail("Nothing to undo.");
        }

        _undo.RemoveLast();
        _redo.Push((node.Value.Label, ProjectSerializer.ToJson(Project, indented: false)));
        Project = ProjectSerializer.FromJson(node.Value.Json);
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return EditResult.Success($"Undo {node.Value.Label}.");
    }

    public EditResult Redo()
    {
        if (_redo.Count == 0)
        {
            return EditResult.Fail("Nothing to redo.");
        }

        var (label, json) = _redo.Pop();
        _undo.AddLast((label, ProjectSerializer.ToJson(Project, indented: false)));
        Project = ProjectSerializer.FromJson(json);
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return EditResult.Success($"Redo {label}.");
    }

    public void Save(string path)
    {
        ProjectSerializer.Save(Project, path);
        FilePath = path;
        IsDirty = false;
    }

    public void MarkClean() => IsDirty = false;
}
