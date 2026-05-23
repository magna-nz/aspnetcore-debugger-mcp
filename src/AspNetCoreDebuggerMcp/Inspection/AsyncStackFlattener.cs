using System.Text.RegularExpressions;

namespace AspNetCoreDebuggerMcp.Inspection;

/// Pure post-processing for stack frames.
///
/// 1. Renames compiler-generated async state-machine frames back to the user's method name:
///    `Ns.Foo.<MethodName>d__3.MoveNext()`  →  `Ns.Foo.MethodName()`
/// 2. Optionally hides pure async/threading infrastructure frames (AsyncTaskMethodBuilder,
///    ExecutionContext.Run, AwaitTaskContinuation, ThreadPool internals, …).
///
/// Line numbers are preserved as-is — we only touch the name string. Lambdas like
/// `<>c.<<Main>$>b__0_0()` are intentionally left alone because they are not the outer method.
public static class AsyncStackFlattener
{
    // Matches `.<Method>d__N.MoveNext` and `.<Method>d__N<TGenericArgs>.MoveNext` —
    // ASP.NET Core / Kestrel pipelines use generic state machines.
    private static readonly Regex MoveNextRx = new(
        @"\.<(?<m>[A-Za-z_][A-Za-z0-9_]*)>d__\d+(?:<[^>]*>)?\.MoveNext",
        RegexOptions.Compiled);

    private static readonly string[] InfrastructurePrefixes = new[]
    {
        "System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
        "System.Runtime.CompilerServices.AsyncMethodBuilderCore",
        "System.Runtime.CompilerServices.AsyncStateMachineBox",
        "System.Threading.ExecutionContext.Run",
        "System.Threading.ExecutionContext.RunInternal",
        "System.Threading.Tasks.AwaitTaskContinuation",
        "System.Threading.Tasks.ContinuationTaskFromTask",
        "System.Threading.Tasks.Task+",
        "System.Threading.ThreadPoolWorkQueue",
        "System.Threading.ThreadPool.",
        "System.Threading.PortableThreadPool",
        "System.Threading.Thread.StartHelper",
    };

    public static IReadOnlyList<StackFrame> Flatten(
        IReadOnlyList<StackFrame> frames, bool hideInfrastructure = true)
    {
        var result = new List<StackFrame>(frames.Count);
        foreach (var f in frames)
        {
            if (hideInfrastructure && IsInfrastructure(f.Name)) continue;
            result.Add(f with { Name = RewriteName(f.Name) });
        }
        return result;
    }

    public static string RewriteName(string name)
        => MoveNextRx.IsMatch(name)
            ? MoveNextRx.Replace(name, m => "." + m.Groups["m"].Value)
            : name;

    public static bool IsInfrastructure(string name)
    {
        foreach (var prefix in InfrastructurePrefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return true;
        return false;
    }
}
