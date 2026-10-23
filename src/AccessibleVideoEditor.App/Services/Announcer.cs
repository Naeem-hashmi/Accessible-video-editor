using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using AccessibleVideoEditor.Core.Settings;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Speaks messages through the user's own screen reader. With NVDA or JAWS running it talks to them
/// directly (most reliable, and it can interrupt); otherwise it raises a Windows UI Automation
/// notification, which Narrator, NVDA, JAWS and others read.
/// </summary>
public sealed class Announcer : IDisposable
{
    private readonly UIElement _host;
    private readonly NvdaClient _nvda = new();
    private readonly JawsClient _jaws = new();

    public Announcer(UIElement host, SpeechOutput mode)
    {
        _host = host;
        Mode = mode;
    }

    public SpeechOutput Mode { get; set; }

    /// <summary>The last message, shown in the status bar so it can be reviewed.</summary>
    public event Action<string>? Spoken;

    /// <summary>Which channel is in use, for the help and status messages.</summary>
    public string ChannelName => Mode == SpeechOutput.Automatic && _nvda.IsRunning() ? "NVDA"
        : Mode == SpeechOutput.Automatic && _jaws.IsRunning() ? "JAWS"
        : "Windows UI Automation";

    public void Say(string text, bool interrupt = true)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Spoken?.Invoke(text);
        if (Mode == SpeechOutput.Automatic)
        {
            if (_nvda.TrySpeak(text, interrupt) || _jaws.TrySpeak(text, interrupt))
            {
                return;
            }
        }

        RaiseNotification(text, interrupt);
    }

    private void RaiseNotification(string text, bool interrupt)
    {
        try
        {
            var peer = UIElementAutomationPeer.FromElement(_host) ?? UIElementAutomationPeer.CreatePeerForElement(_host);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                interrupt ? AutomationNotificationProcessing.ImportantMostRecent : AutomationNotificationProcessing.ImportantAll,
                text,
                "ClipScribe");
        }
        catch (InvalidOperationException ex)
        {
            AppPaths.Log("UIA notification failed: " + ex.Message);
        }
    }

    public void Dispose() => _nvda.Dispose();
}

/// <summary>
/// NVDA's controller client (nvdaControllerClient.dll from NV Access, LGPL), loaded from the program
/// folder if present. Missing DLL or NVDA not running simply means "not available".
/// </summary>
internal sealed class NvdaClient : IDisposable
{
    private readonly IntPtr _library;
    private readonly TestIfRunning? _test;
    private readonly SpeakText? _speak;
    private readonly CancelSpeech? _cancel;

    public NvdaClient()
    {
        foreach (var name in new[] { "nvdaControllerClient.dll", "nvdaControllerClient64.dll" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out _library))
            {
                break;
            }
        }

        if (_library == IntPtr.Zero)
        {
            return;
        }

        _test = Get<TestIfRunning>("nvdaController_testIfRunning");
        _speak = Get<SpeakText>("nvdaController_speakText");
        _cancel = Get<CancelSpeech>("nvdaController_cancelSpeech");
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int TestIfRunning();

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SpeakText([MarshalAs(UnmanagedType.LPWStr)] string text);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CancelSpeech();

    public bool IsRunning()
    {
        try
        {
            return _test is not null && _test() == 0;
        }
        catch (SEHException)
        {
            return false;
        }
    }

    public bool TrySpeak(string text, bool interrupt)
    {
        if (_speak is null || !IsRunning())
        {
            return false;
        }

        try
        {
            if (interrupt)
            {
                _cancel?.Invoke();
            }

            return _speak(text) == 0;
        }
        catch (SEHException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_library != IntPtr.Zero)
        {
            NativeLibrary.Free(_library);
        }
    }

    private T? Get<T>(string name) where T : Delegate =>
        NativeLibrary.TryGetExport(_library, name, out var address) ? Marshal.GetDelegateForFunctionPointer<T>(address) : null;
}

/// <summary>JAWS through its COM interface (FreedomSci.JawsApi), which is installed with JAWS.</summary>
internal sealed class JawsClient
{
    private object? _api;
    private bool _unavailable;
    private DateTime _lastCheck = DateTime.MinValue;
    private bool _running;

    public bool IsRunning()
    {
        if ((DateTime.UtcNow - _lastCheck).TotalSeconds > 3)
        {
            _lastCheck = DateTime.UtcNow;
            _running = Process.GetProcessesByName("jfw").Length > 0;
        }

        return _running;
    }

    public bool TrySpeak(string text, bool interrupt)
    {
        if (_unavailable || !IsRunning())
        {
            return false;
        }

        try
        {
            if (_api is null)
            {
                var type = Type.GetTypeFromProgID("FreedomSci.JawsApi");
                if (type is null)
                {
                    _unavailable = true;
                    return false;
                }

                _api = Activator.CreateInstance(type);
            }

            dynamic api = _api!;
            return (bool)api.SayString(text, interrupt);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.Reflection.TargetInvocationException)
        {
            AppPaths.Log("JAWS speech failed: " + ex.Message);
            _api = null;
            return false;
        }
    }
}
