using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace TaskApi
{
    public class RabbitMqTaskEventPublisher : ITaskEventPublisher, IHostedService, IDisposable
    {
        private readonly ILogger<RabbitMqTaskEventPublisher> _logger;
        private readonly RabbitMqOptions _options;
        private readonly Channel<TaskCompletedEvent> _channel;
        private readonly JsonSerializerOptions _jsonOptions;

        private Task? _worker;
        private CancellationTokenSource? _cts;
        private IConnection? _connection;
        private IModel? _rabbitChannel;

        public RabbitMqTaskEventPublisher(
            IOptions<RabbitMqOptions> options,
            ILogger<RabbitMqTaskEventPublisher> logger)
        {
            _logger = logger;
            _options = options.Value;

            _channel = Channel.CreateBounded<TaskCompletedEvent>(new BoundedChannelOptions(1000)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public async ValueTask PublishAsync(TaskCompletedEvent evt, CancellationToken ct = default)
        {
            await _channel.Writer.WriteAsync(evt, ct);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => ProcessQueueAsync(), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _channel.Writer.TryComplete();

            if (_worker is not null)
            {
                var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                var completed = await Task.WhenAny(_worker, timeout);

                if (completed == timeout)
                    _logger.LogWarning("RabbitMQ publisher did not drain within shutdown timeout");
            }

            _cts?.Cancel();
            CloseConnection();
        }

        private async Task ProcessQueueAsync()
        {
            await foreach (var evt in _channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                PublishToRabbit(evt);
            }
        }

        private void PublishToRabbit(TaskCompletedEvent evt)
        {
            try
            {
                EnsureConnected();

                if (_rabbitChannel is null)
                    return;

                var json = JsonSerializer.Serialize(evt, _jsonOptions);
                var body = Encoding.UTF8.GetBytes(json);

                var props = _rabbitChannel.CreateBasicProperties();
                props.DeliveryMode = 2;
                props.ContentType = "application/json";

                _rabbitChannel.BasicPublish(
                    exchange: _options.ExchangeName,
                    routingKey: _options.RoutingKey,
                    basicProperties: props,
                    body: body);

                _logger.LogInformation("Published task.completed for task {TaskId}", evt.TaskId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to publish task.completed for task {TaskId}. Event: {@Event}",
                    evt.TaskId, evt);

                CloseConnection();
            }
        }

        private void EnsureConnected()
        {
            if (_connection?.IsOpen == true && _rabbitChannel?.IsOpen == true)
                return;

            CloseConnection();

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(2)
            };

            _connection = factory.CreateConnection();
            _rabbitChannel = _connection.CreateModel();

            _rabbitChannel.ExchangeDeclare(
                exchange: _options.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);
        }

        private void CloseConnection()
        {
            try { _rabbitChannel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }

            _rabbitChannel?.Dispose();
            _connection?.Dispose();

            _rabbitChannel = null;
            _connection = null;
        }

        public void Dispose()
        {
            _cts?.Dispose();
            CloseConnection();
        }
    }
}
