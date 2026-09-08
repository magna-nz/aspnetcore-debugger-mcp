var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/users/{id:int}", (int id) =>
{
    var name = $"User{id}";
    return Results.Ok(new { id, name });
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    environment = app.Environment.EnvironmentName,
    sampleVar = Environment.GetEnvironmentVariable("SAMPLE_VAR"),
}));

// Deep recursion target for stack-trace-at-scale tests. `depth` recursive
// frames sit between the handler and the base case a breakpoint lands on.
app.MapGet("/recurse/{depth:int}", (int depth) =>
{
    var result = Recurse(depth);
    return Results.Ok(new { depth, result });
});

app.Run();

static int Recurse(int n)
{
    if (n <= 0)
    {
        return 0;
    }
    return 1 + Recurse(n - 1);
}
