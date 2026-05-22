namespace AspNetCoreDebuggerMcp.Debugging;

public sealed class DebugException : Exception
{
    public DebugException(string message) : base(message) { }
    public DebugException(string message, Exception inner) : base(message, inner) { }
}
