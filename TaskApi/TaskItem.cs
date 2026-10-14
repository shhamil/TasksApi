namespace TaskApi
{
    public enum Priority
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public class TaskItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public Priority Priority { get; set; }
    }
}
