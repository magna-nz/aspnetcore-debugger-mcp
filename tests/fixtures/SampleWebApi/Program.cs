var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/users/{id:int}", (int id) =>
{
    var name = $"User{id}";
    return Results.Ok(new { id, name });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
