using muten.Core;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

using var manager = new AudioSessionManager();

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "list":
        return ListSessions(manager);

    case "mute":
        return HandleMuteCommand(manager, args.Skip(1).ToArray(), mute: true);

    case "unmute":
        return HandleMuteCommand(manager, args.Skip(1).ToArray(), mute: false);

    case "toggle":
        return HandleToggleCommand(manager, args.Skip(1).ToArray());

    case "volume":
        return HandleVolumeCommand(manager, args.Skip(1).ToArray());

    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
}

static int ListSessions(AudioSessionManager manager)
{
    var sessions = manager.GetSessions();

    if (sessions.Count == 0)
    {
        Console.WriteLine("No active audio sessions.");
        return 0;
    }

    Console.WriteLine($"{"PID",-8} {"Name",-25} {"Volume",7} {"Muted",6}");
    Console.WriteLine(new string('-', 50));

    foreach (var session in sessions)
    {
        var vol = $"{session.Volume * 100:F0}%";
        var muted = session.IsMuted ? "Yes" : "No";
        Console.WriteLine($"{session.ProcessId,-8} {session.ProcessName,-25} {vol,7} {muted,6}");
    }

    return 0;
}

static int HandleMuteCommand(AudioSessionManager manager, string[] args, bool mute)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine($"Usage: muten {(mute ? "mute" : "unmute")} <name|pid>");
        return 1;
    }

    var target = string.Join(' ', args);
    bool success;

    if (int.TryParse(target, out var pid))
        success = mute ? manager.Mute(pid) : manager.Unmute(pid);
    else
        success = mute ? manager.MuteByName(target) : manager.UnmuteByName(target);

    if (!success)
    {
        Console.Error.WriteLine($"No audio session found for: {target}");
        return 1;
    }

    Console.WriteLine($"{(mute ? "Muted" : "Unmuted")}: {target}");
    return 0;
}

static int HandleToggleCommand(AudioSessionManager manager, string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Usage: muten toggle <name|pid>");
        return 1;
    }

    var target = string.Join(' ', args);
    bool success;

    if (int.TryParse(target, out var pid))
        success = manager.ToggleMute(pid);
    else
        success = manager.ToggleMuteByName(target);

    if (!success)
    {
        Console.Error.WriteLine($"No audio session found for: {target}");
        return 1;
    }

    Console.WriteLine($"Toggled mute: {target}");
    return 0;
}

static int HandleVolumeCommand(AudioSessionManager manager, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: muten volume <name|pid> <0-100>");
        return 1;
    }

    if (!int.TryParse(args[^1], out var percent) || percent < 0 || percent > 100)
    {
        Console.Error.WriteLine("Volume must be a number between 0 and 100.");
        return 1;
    }

    var target = string.Join(' ', args[..^1]);
    var volume = percent / 100f;
    bool success;

    if (int.TryParse(target, out var pid))
        success = manager.SetVolume(pid, volume);
    else
        success = manager.SetVolumeByName(target, volume);

    if (!success)
    {
        Console.Error.WriteLine($"No audio session found for: {target}");
        return 1;
    }

    Console.WriteLine($"Set volume to {percent}%: {target}");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("""
        muten - Control application audio from the command line

        Usage:
          muten list                       List all active audio sessions
          muten mute <name|pid>            Mute an application
          muten unmute <name|pid>          Unmute an application
          muten toggle <name|pid>          Toggle mute for an application
          muten volume <name|pid> <0-100>  Set volume for an application

        Examples:
          muten list
          muten mute spotify
          muten unmute 12345
          muten toggle chrome
          muten volume discord 50
        """);
}
