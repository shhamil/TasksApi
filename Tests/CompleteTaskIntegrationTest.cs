using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskApi;

namespace Tests
{
    public class CompleteTaskIntegrationTest : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;

        public CompleteTaskIntegrationTest(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task CompleteTask_PublishesMessage()
        {
            var client = _factory.CreateClient();

            var createResponse = await client.PostAsJsonAsync("/tasks", new
            {
                title = "Buy milk",
                priority = 2
            });

            createResponse.EnsureSuccessStatusCode();

            var created = await createResponse.Content.ReadFromJsonAsync<TaskItem>();
            Assert.NotNull(created);
            Assert.False(created.IsCompleted);
            Assert.Null(created.CompletedAt);

            var completeResponse = await client.PutAsync($"/tasks/{created.Id}/complete", null);
            Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

            var publisher = _factory.Services.GetRequiredService<FakeTaskEventPublisher>();
            var message = Assert.Single(publisher.Messages);

            Assert.Equal(created.Id, message.TaskId);
            Assert.Equal("Buy milk", message.Title);
            Assert.Equal(Priority.High, message.Priority);
            Assert.NotEqual(default, message.CompletedAt);
        }
    }


    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbName = $"TestDb_{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TaskDbContext>>();
                services.RemoveAll<TaskDbContext>();

                var dbContextConfigs = services
                    .Where(d => d.ServiceType.IsGenericType &&
                                d.ServiceType.GetGenericTypeDefinition().Name
                                    .StartsWith("IDbContextOptionsConfiguration"))
                    .ToList();

                foreach (var d in dbContextConfigs)
                    services.Remove(d);

                services.AddDbContext<TaskDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName));

                services.RemoveAll<ITaskEventPublisher>();
                services.AddSingleton<FakeTaskEventPublisher>();
                services.AddSingleton<ITaskEventPublisher>(sp =>
                    sp.GetRequiredService<FakeTaskEventPublisher>());
            });
        }
    }


    public class FakeTaskEventPublisher : ITaskEventPublisher
    {
        public List<TaskCompletedEvent> Messages { get; } = new();

        public ValueTask PublishAsync(TaskCompletedEvent evt, CancellationToken ct = default)
        {
            Messages.Add(evt);
            return ValueTask.CompletedTask;
        }
    }
}
