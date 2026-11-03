using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace JvDataCollector;

public sealed class JvGateway : IJvGateway
{
    private readonly IAppLog _log;
    private readonly object _com;
    private JvEventSink? _sink;
    private IConnectionPoint? _point;
    private int _cookie;
    private bool _watching;

    public JvGateway(IAppLog log)
    {
        _log = log;
        var type = Type.GetTypeFromProgID("JVDTLab.JVLink", throwOnError: false)
            ?? throw new InvalidOperationException("64bit の JV-Link が登録されていません。");
        _com = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("JV-Link を生成できません。");
    }

    public int Init(string softwareId) => Call("JVInit", [softwareId]);

    public int SetServiceKey(string serviceKey) => Call("JVSetServiceKey", [serviceKey]);

    public int SetSavePath(string path) => Call("JVSetSavePath", [path]);

    public int SetSaveFlag(int flag) => Call("JVSetSaveFlag", [flag]);

    public int SetUiProperties() => Call("JVSetUIProperties");

    public OpenResult Open(string dataspec, string fromTime, int option)
    {
        var args = new object?[] { dataspec, fromTime, option, 0, 0, "" };
        var code = Call("JVOpen", args, 3, 4, 5);
        return new OpenResult(code, AsInt(args[3]), AsInt(args[4]), args[5] as string);
    }

    public OpenResult RtOpen(string dataspec, string key)
    {
        var code = Call("JVRTOpen", [dataspec, key]);
        return new OpenResult(code, 0, 0, null);
    }

    public int Status() => Call("JVStatus");

    public ReadResult Read()
    {
        var args = new object?[] { new byte[1], 110000, "" };
        var code = Call("JVGets", args, 0, 2);
        var name = args[2] as string;
        if (code <= 0)
        {
            return new ReadResult(code, null, name);
        }

        if (args[0] is not byte[] bytes || bytes.Length < code)
        {
            _log.Error("JVGets のバイト列を取得できません。");
            return new ReadResult(-1_000_001, null, name);
        }

        if (bytes.Length == code)
        {
            return new ReadResult(code, bytes, name);
        }

        var copy = new byte[code];
        Buffer.BlockCopy(bytes, 0, copy, 0, code);
        return new ReadResult(code, copy, name);
    }

    public void Skip() => Call("JVSkip");

    public void CancelDownload() => Call("JVCancel");

    public int Close() => Call("JVClose");

    public int DeleteFile(string fileName) => Call("JVFiledelete", [fileName]);

    public string? CurrentFileTimeStamp()
    {
        try
        {
            return _com.GetType().InvokeMember(
                "m_CurrentFileTimeStamp",
                BindingFlags.GetProperty,
                null,
                _com,
                null,
                CultureInfo.InvariantCulture) as string;
        }
        catch (Exception ex)
        {
            _log.Error(ex.GetType().Name);
            return null;
        }
    }

    public void StartWatch(Action<RealtimeEventKind, string> onEvent)
    {
        try
        {
            _sink ??= new JvEventSink();
            _sink.Handler = onEvent;
            if (_point is null)
            {
                var container = (IConnectionPointContainer)_com;
                var iid = typeof(IJvLinkEventSink).GUID;
                container.FindConnectionPoint(ref iid, out var point);
                if (point is null)
                {
                    _log.Error("FindConnectionPoint");
                    return;
                }

                _point = point;
                _point.Advise(_sink, out _cookie);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex.GetType().Name);
            return;
        }

        if (_watching)
        {
            return;
        }

        var code = Call("JVWatchEvent");
        if (code == 0)
        {
            _watching = true;
        }
        else
        {
            _log.Info($"JVWatchEvent {code}");
        }
    }

    public void StopWatch()
    {
        if (!_watching)
        {
            return;
        }

        Call("JVWatchEventClose");
        _watching = false;
    }

    private int Call(string name, object?[]? args = null, params int[] byRef)
    {
        args ??= [];
        try
        {
            ParameterModifier[]? modifiers = null;
            if (byRef.Length > 0)
            {
                var modifier = new ParameterModifier(args.Length);
                foreach (var index in byRef)
                {
                    modifier[index] = true;
                }

                modifiers = [modifier];
            }

            var result = _com.GetType().InvokeMember(
                name,
                BindingFlags.InvokeMethod,
                null,
                _com,
                args,
                modifiers,
                CultureInfo.InvariantCulture,
                null);
            return AsInt(result);
        }
        catch (Exception ex)
        {
            _log.Error($"{name} {ex.GetType().Name}");
            return -1_000_002;
        }
    }

    private static int AsInt(object? value) => value switch
    {
        null => 0,
        int number => number,
        short number => number,
        long number => checked((int)number),
        _ => Convert.ToInt32(value, CultureInfo.InvariantCulture),
    };
}

[ComVisible(true)]
[Guid("17E1E656-828B-4849-B043-FA62B92D9E41")]
[InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IJvLinkEventSink
{
    [DispId(1)]
    void JVEvtPay(string bstr);

    [DispId(2)]
    void JVEvtJockeyChange(string bstr);

    [DispId(3)]
    void JVEvtWeather(string bstr);

    [DispId(4)]
    void JVEvtCourseChange(string bstr);

    [DispId(5)]
    void JVEvtAvoid(string bstr);

    [DispId(6)]
    void JVEvtTimeChange(string bstr);

    [DispId(7)]
    void JVEvtWeight(string bstr);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class JvEventSink : IJvLinkEventSink
{
    public Action<RealtimeEventKind, string>? Handler { get; set; }

    public void JVEvtPay(string bstr) => Raise(RealtimeEventKind.Payoff, bstr);

    public void JVEvtJockeyChange(string bstr) => Raise(RealtimeEventKind.JockeyChange, bstr);

    public void JVEvtWeather(string bstr) => Raise(RealtimeEventKind.Weather, bstr);

    public void JVEvtCourseChange(string bstr) => Raise(RealtimeEventKind.CourseChange, bstr);

    public void JVEvtAvoid(string bstr) => Raise(RealtimeEventKind.Scratch, bstr);

    public void JVEvtTimeChange(string bstr) => Raise(RealtimeEventKind.PostTime, bstr);

    public void JVEvtWeight(string bstr) => Raise(RealtimeEventKind.Weight, bstr);

    private void Raise(RealtimeEventKind kind, string? key)
    {
        try
        {
            Handler?.Invoke(kind, key ?? "");
        }
        catch (Exception)
        {
            // コールバックはキューへ入れるだけ。ここで取得は始めない。
        }
    }
}
