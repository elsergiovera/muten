namespace muten.Core;

public class AudioSession
{
    public int ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public float Volume { get; init; }
    public bool IsMuted { get; init; }
    public bool IsActive { get; init; }
    public string? ExecutablePath { get; init; }
}
