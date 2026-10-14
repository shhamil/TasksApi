using Microsoft.EntityFrameworkCore;
using TaskApi;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));


builder.Services.AddSingleton<RabbitMqTaskEventPublisher>();
builder.Services.AddSingleton<ITaskEventPublisher>(sp =>
    sp.GetRequiredService<RabbitMqTaskEventPublisher>());

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService(sp =>
        sp.GetRequiredService<RabbitMqTaskEventPublisher>());
}

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    for (var attempt = 1; attempt <= 10; attempt++)
    {
        try
        {
            db.Database.EnsureCreated();
            break;
        }
        catch (Exception ex) when (attempt < 10)
        {
            logger.LogWarning(ex, "DB not ready, retry {Attempt}/10", attempt);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}


app.MapPost("/tasks", async (CreateTaskRequest req, TaskDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 200)
        return Results.BadRequest(new { error = "Title must be non-empty and <= 200 characters" });

    var task = new TaskItem
    {
        Id = Guid.NewGuid(),
        Title = req.Title.Trim(),
        IsCompleted = false,
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = null,
        Priority = req.Priority ?? Priority.Medium
    };

    db.Tasks.Add(task);
    await db.SaveChangesAsync();

    return Results.Created($"/tasks/{task.Id}", task);
});

app.MapGet("/tasks", async (TaskDbContext db) =>
    Results.Ok(await db.Tasks.AsNoTracking().ToListAsync()));

app.MapPut("/tasks/{id:guid}/complete", async (
    Guid id,
    TaskDbContext db,
    ITaskEventPublisher publisher) =>
{
    var task = await db.Tasks.FindAsync(id);

    if (task is null)
        return Results.NotFound();

    if (task.IsCompleted)
        return Results.Conflict(new { error = "Task already completed" });

    task.IsCompleted = true;
    task.CompletedAt = DateTimeOffset.UtcNow;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Task was modified by another request" });
    }

    await publisher.PublishAsync(new TaskCompletedEvent(
        task.Id,
        task.Title,
        task.CompletedAt.Value,
        task.Priority));

    return Results.Ok(task);
});

app.MapDelete("/tasks/{id:guid}", async (Guid id, TaskDbContext db) =>
{
    var task = await db.Tasks.FindAsync(id);

    if (task is null)
        return Results.NotFound();

    db.Tasks.Remove(task);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.Run();

public partial class Program { }