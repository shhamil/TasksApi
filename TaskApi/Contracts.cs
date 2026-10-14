namespace TaskApi
{
    public record CreateTaskRequest(string Title, Priority? Priority);

    public record TaskCompletedEvent(
        Guid TaskId,
        string Title,
        DateTimeOffset CompletedAt,
        Priority Priority);

    public interface ITaskEventPublisher
    {
        ValueTask PublishAsync(TaskCompletedEvent evt, CancellationToken ct = default);
    }

    public class RabbitMqOptions
    {
        public string HostName { get; set; } = "localhost";
        public int Port { get; set; } = 5672;
        public string UserName { get; set; } = "guest";
        public string Password { get; set; } = "guest";
        public string ExchangeName { get; set; } = "task.events";
        public string RoutingKey { get; set; } = "task.completed";
    }
}
