using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

/// <summary>
/// Platform-aware string marshaller for hostfxr interop.
/// Uses UTF-16 on Windows and UTF-8 on Unix, matching hostfxr's char_t type.
/// </summary>
[CustomMarshaller(typeof(string), MarshalMode.Default, typeof(PlatformStringMarshaller))]
static unsafe class PlatformStringMarshaller
{
    public static nint ConvertToUnmanaged(string? managed)
    {
        if (managed is null)
            return 0;

        return OperatingSystem.IsWindows()
            ? (nint)Utf16StringMarshaller.ConvertToUnmanaged(managed)
            : (nint)Utf8StringMarshaller.ConvertToUnmanaged(managed);
    }

    public static string? ConvertToManaged(nint unmanaged)
    {
        if (unmanaged == 0)
            return null;

        return OperatingSystem.IsWindows()
            ? Utf16StringMarshaller.ConvertToManaged((ushort*)unmanaged)
            : Utf8StringMarshaller.ConvertToManaged((byte*)unmanaged);
    }

    public static void Free(nint unmanaged)
    {
        if (unmanaged == 0)
            return;

        if (OperatingSystem.IsWindows())
            Utf16StringMarshaller.Free((ushort*)unmanaged);
        else
            Utf8StringMarshaller.Free((byte*)unmanaged);
    }
}

/// <summary>
/// Lightweight wrapper for reading platform-dependent string pointers from native memory.
/// A null pointer converts to <see cref="string.Empty"/>, not <c>null</c>.
/// </summary>
readonly struct PlatformString
{
    public nint Value { get; }

    public override readonly string ToString()
    {
        if (Value == 0)
            return string.Empty;

        if (!OperatingSystem.IsWindows())
            return Marshal.PtrToStringUTF8(Value) ?? string.Empty;

        return Marshal.PtrToStringUni(Value) ?? string.Empty;
    }

    public static implicit operator string(PlatformString value) => value.ToString();
}
