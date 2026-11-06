using System.Runtime.InteropServices;

namespace muten.Tray;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

// A global key combination such as Ctrl+Alt+M, stored in settings as text
public readonly record struct Hotkey(HotkeyModifiers Modifiers, Keys Key)
{
    public static readonly Hotkey Default = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.M);

    // Combos that would swallow normal typing (a letter alone, Shift+letter) are not allowed
    public bool IsValid =>
        Key != Keys.None && !IsModifierKey(Key)
        && ((Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0
            || Key is >= Keys.F1 and <= Keys.F24 || Key == Keys.Pause);

    public static bool IsModifierKey(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
        or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu
        or Keys.LWin or Keys.RWin;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static readonly Dictionary<Keys, string> Names = new()
    {
        [Keys.Oemtilde] = "`", [Keys.OemMinus] = "-", [Keys.Oemplus] = "=", [Keys.OemOpenBrackets] = "[",
        [Keys.OemCloseBrackets] = "]", [Keys.OemPipe] = "\\", [Keys.OemSemicolon] = ";", [Keys.OemQuotes] = "'",
        [Keys.Oemcomma] = ",", [Keys.OemPeriod] = ".", [Keys.OemQuestion] = "/", [Keys.Next] = "PageDown",
        [Keys.Prior] = "PageUp", [Keys.Return] = "Enter", [Keys.Back] = "Backspace", [Keys.Capital] = "CapsLock",
    };

    private static string KeyName(Keys key)
    {
        if (Names.TryGetValue(key, out var name)) return name;
        if (key is >= Keys.D0 and <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
        return key.ToString();
    }

    // "Ctrl+Alt+M" -> hotkey; null for empty or unparseable text
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        Keys key = Keys.None;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            // A trailing empty part means the key itself was "+" (e.g. "Ctrl++"); treat as "="
            if (part.Length == 0) { if (i == parts.Length - 1) key = Keys.Oemplus; continue; }

            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Control; continue;
                case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": modifiers |= HotkeyModifiers.Win; continue;
            }

            if (i != parts.Length - 1) return null;
            key = ParseKey(part);
        }

        var hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid ? hotkey : null;
    }

    private static Keys ParseKey(string part)
    {
        var named = Names.FirstOrDefault(kv => kv.Value.Equals(part, StringComparison.OrdinalIgnoreCase));
        if (named.Value is not null) return named.Key;
        if (part.Length == 1 && char.IsDigit(part[0])) return Keys.D0 + (part[0] - '0');
        return Enum.TryParse<Keys>(part, ignoreCase: true, out var key) && (key & Keys.Modifiers) == 0 ? key : Keys.None;
    }
}

// Registers one global hotkey on a message-only window and raises Pressed on the UI thread.
// Create and use it on the UI thread.
public sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly int _id;
    private static int _nextId = 0x4D00;
    private bool _registered;
    private bool _disposed;

    public Hotkey? Current { get; private set; }

    public event Action? Pressed;

    public GlobalHotkey()
    {
        _id = Interlocked.Increment(ref _nextId);
        CreateHandle(new CreateParams { Caption = "muten-hotkey", Parent = HWND_MESSAGE });
    }

    // Replaces the registered hotkey (null clears it). When the new one cannot be registered the previous one is
    // registered again, so a failed change never leaves muten without its hotkey.
    public bool TrySet(Hotkey? hotkey, out string? error)
    {
        error = null;
        ObjectDisposedException.ThrowIf(_disposed, this);

        var previous = Current;
        Unregister();

        if (hotkey is null) return true;

        if (!hotkey.Value.IsValid)
        {
            error = $"{hotkey} can't be used as a hotkey.";
            Restore(previous);
            return false;
        }

        if (Register(hotkey.Value, out int code))
            return true;

        error = code == ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"{hotkey} is already used by another app."
            : $"{hotkey} could not be registered (error {code}).";
        Restore(previous);
        return false;
    }

    private void Restore(Hotkey? previous)
    {
        if (previous is not null) Register(previous.Value, out _);
    }

    private bool Register(Hotkey hotkey, out int errorCode)
    {
        errorCode = 0;
        if (RegisterHotKey(Handle, _id, (uint)hotkey.Modifiers | MOD_NOREPEAT, (uint)hotkey.Key))
        {
            _registered = true;
            Current = hotkey;
            return true;
        }

        errorCode = Marshal.GetLastWin32Error();
        return false;
    }

    public void Unregister()
    {
        if (_registered && Handle != IntPtr.Zero)
            UnregisterHotKey(Handle, _id);
        _registered = false;
        Current = null;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)_id)
        {
            try { Pressed?.Invoke(); } catch { }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        DestroyHandle();
        _disposed = true;
    }
}
