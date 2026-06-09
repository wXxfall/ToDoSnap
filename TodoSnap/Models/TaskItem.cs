namespace TodoSnap.Models;

/// <summary>A single to-do item persisted to tasks.json.</summary>
public class TaskItem
{
    /// <summary>Short 8-char id derived from a GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>The distilled one-line description shown in the floating bar.</summary>
    public string Description { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Completion flag, kept for future use (completed items are removed today).</summary>
    public bool Done { get; set; } = false;
}
