using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Tests.Inspection;

public class AsyncStackFlattenerTests
{
    [Theory]
    [InlineData("MyApp.UserService.<GetUserAsync>d__3.MoveNext()", "MyApp.UserService.GetUserAsync()")]
    [InlineData("MyApp.Foo.<DoAsync>d__7.MoveNext", "MyApp.Foo.DoAsync")]
    [InlineData("MyApp.Controllers.UserController.<GetAsync>d__5.MoveNext()", "MyApp.Controllers.UserController.GetAsync()")]
    [InlineData(
        "Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpProtocol.<ProcessRequests>d__237<Microsoft.AspNetCore.Hosting.HostingApplication.Context>.MoveNext()",
        "Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpProtocol.ProcessRequests()")]
    public void RewriteName_StateMachineToOriginal(string input, string expected)
    {
        Assert.Equal(expected, AsyncStackFlattener.RewriteName(input));
    }

    [Theory]
    [InlineData("Program.<>c.<<Main>$>b__0_0()")]            // top-level lambda — NOT a state machine
    [InlineData("MyApp.Foo.Bar()")]                            // ordinary method
    [InlineData("MyApp.Foo.<>c__DisplayClass1_0.<Bar>b__0()")] // closure-class lambda
    public void RewriteName_DoesNotTouchLambdasOrPlainMethods(string input)
    {
        Assert.Equal(input, AsyncStackFlattener.RewriteName(input));
    }

    [Theory]
    [InlineData("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.Start[TStateMachine](ref TStateMachine)", true)]
    [InlineData("System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start", true)]
    [InlineData("System.Threading.ExecutionContext.RunInternal(ExecutionContext, ContextCallback, object)", true)]
    [InlineData("System.Threading.Tasks.AwaitTaskContinuation.RunOrScheduleAction(IAsyncStateMachineBox, bool)", true)]
    [InlineData("System.Threading.ThreadPoolWorkQueue.Dispatch()", true)]
    [InlineData("MyApp.UserService.GetUserAsync()", false)]
    [InlineData("System.Linq.Enumerable.Where[T](IEnumerable`1, Func`2)", false)]
    public void IsInfrastructure_MatchesKnownFramesOnly(string name, bool expected)
    {
        Assert.Equal(expected, AsyncStackFlattener.IsInfrastructure(name));
    }

    [Fact]
    public void Flatten_RewritesStateMachinesAndDropsInfrastructure()
    {
        var frames = new[]
        {
            new StackFrame(1, "MyApp.UserRepository.<GetByIdAsync>d__2.MoveNext()", "/repo.cs", 42, null),
            new StackFrame(2, "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1.Start[T]", null, null, null),
            new StackFrame(3, "MyApp.UserService.<GetUserAsync>d__3.MoveNext()", "/svc.cs", 17, null),
            new StackFrame(4, "System.Threading.ExecutionContext.RunInternal", null, null, null),
            new StackFrame(5, "MyApp.UserController.<GetAsync>d__5.MoveNext()", "/ctrl.cs", 9, null),
        };

        var flat = AsyncStackFlattener.Flatten(frames, hideInfrastructure: true);

        Assert.Equal(3, flat.Count);
        Assert.Equal("MyApp.UserRepository.GetByIdAsync()", flat[0].Name);
        Assert.Equal("MyApp.UserService.GetUserAsync()", flat[1].Name);
        Assert.Equal("MyApp.UserController.GetAsync()", flat[2].Name);
        Assert.Equal(42, flat[0].Line);   // line numbers preserved
        Assert.Equal(17, flat[1].Line);
        Assert.Equal(9, flat[2].Line);
    }

    [Fact]
    public void Flatten_HideInfrastructureFalse_KeepsAllFrames()
    {
        var frames = new[]
        {
            new StackFrame(1, "MyApp.UserService.<GetAsync>d__3.MoveNext()", null, null, null),
            new StackFrame(2, "System.Threading.ExecutionContext.RunInternal", null, null, null),
        };

        var flat = AsyncStackFlattener.Flatten(frames, hideInfrastructure: false);

        Assert.Equal(2, flat.Count);
        Assert.Equal("MyApp.UserService.GetAsync()", flat[0].Name);              // still rewritten
        Assert.Equal("System.Threading.ExecutionContext.RunInternal", flat[1].Name); // kept
    }
}
