namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// The outcome of an edit. <see cref="Message"/> is spoken to the user. When <see cref="Ok"/> is false
/// the project is left unchanged.
/// </summary>
public sealed record EditResult(bool Ok, string Message)
{
    /// <summary>The item that should have focus afterwards, if it changed.</summary>
    public Guid? FocusItemId { get; init; }

    /// <summary>The layer that should have focus afterwards, if it changed.</summary>
    public Guid? FocusLayerId { get; init; }

    public static EditResult Success(string message, Guid? focusItem = null, Guid? focusLayer = null) =>
        new(true, message) { FocusItemId = focusItem, FocusLayerId = focusLayer };

    public static EditResult Fail(string message) => new(false, message);
}
