using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Captures hostfxr error messages for the lifetime of the scope.
/// Use <see cref="Drain"/> after each hostfxr call to get messages for that call.
/// </summary>
/// <remarks>
/// Captures can be nested but must be disposed in LIFO (stack) order.
/// Use <c>using</c> statements to guarantee correct ordering.
/// </remarks>
sealed unsafe class ErrorCapture : IDisposable
{
    [ThreadStatic]
    private static ErrorCapture? t_current;

    private readonly ErrorCapture? _previous;
    private readonly nint _previousWriter;
    private readonly List<string> _messages = [];
    private bool _disposed;

    internal ErrorCapture()
    {
        _previous = t_current;
        t_current = this;
        _previousWriter = HostFxr.SetErrorWriter(&OnError);
    }

    public IReadOnlyList<string> Messages => _messages;

    /// <summary>
    /// Returns captured messages and clears the list.
    /// Call this after each hostfxr operation to attribute messages to that call.
    /// </summary>
    public string[] Drain()
    {
        var result = _messages.ToArray();
        _messages.Clear();
        return result;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(nint message)
    {
        var capture = t_current;
        if (capture is null) return;

        string? str = PlatformStringMarshaller.ConvertToManaged(message);
        if (str is not null)
            capture._messages.Add(str);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Debug.Assert(t_current == this, "ErrorCapture disposed out of LIFO order.");
        HostFxr.SetErrorWriter((delegate* unmanaged[Cdecl]<nint, void>)_previousWriter);
        t_current = _previous;
    }
}
