using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Events;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Shared.Queue
{
    public class RabbitMQEventBus : IEventBus
    {
        private readonly string _hostName;
        private readonly string _username;
        private readonly string _password;
        private readonly string _exchangeName = "smartcore_exchange";
        private IConnection _connection;
        private IChannel _channel;
        private readonly IServiceProvider _serviceProvider;
        private readonly System.Threading.SemaphoreSlim _connectionSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        public RabbitMQEventBus(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _hostName = configuration["RabbitMQ:Host"] ?? "localhost";
            _username = configuration["RabbitMQ:Username"] ?? "guest";
            _password = configuration["RabbitMQ:Password"] ?? "guest";
            _serviceProvider = serviceProvider;
            // Try initial connection in constructor, but do not block startup completely if it fails
            _ = Task.Run(async () => {
                try
                {
                    await EnsureConnectionAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RabbitMQEventBus] Initial constructor connection failed: {ex.Message}");
                }
            });
        }

        private async Task EnsureConnectionAsync()
        {
            if (_channel != null && _channel.IsOpen)
            {
                return;
            }

            await _connectionSemaphore.WaitAsync();
            try
            {
                if (_channel != null && _channel.IsOpen)
                {
                    return;
                }

                Console.WriteLine("[RabbitMQEventBus] RabbitMQ channel is closed or uninitialized. Reconnecting...");
                await InitializeConnectionAsync();
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task InitializeConnectionAsync()
        {
            var factory = new ConnectionFactory()
            {
                HostName = _hostName,
                UserName = _username,
                Password = _password
            };

            // Clean up old instances if they exist
            try
            {
                if (_channel != null)
                {
                    try { await _channel.CloseAsync(); } catch {}
                }
                if (_connection != null)
                {
                    try { await _connection.CloseAsync(); } catch {}
                }
            }
            catch {}

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync();
                    _channel = await _connection.CreateChannelAsync();
                    await _channel.ExchangeDeclareAsync(_exchangeName, ExchangeType.Direct, durable: true);
                    Console.WriteLine("[RabbitMQEventBus] Successfully connected to RabbitMQ and declared exchange.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RabbitMQ Connection retry {i+1}/5 failed: {ex.Message}");
                    await Task.Delay(2000);
                }
            }
        }

        public async Task PublishAsync<T>(T @event) where T : IntegrationEvent
        {
            await EnsureConnectionAsync();

            if (_channel == null)
            {
                throw new InvalidOperationException("RabbitMQ channel is not initialized.");
            }

            var routingKey = typeof(T).Name;
            var message = JsonSerializer.Serialize(@event);
            var body = Encoding.UTF8.GetBytes(message);

            var properties = new BasicProperties
            {
                Persistent = true
            };

            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body
            );
        }

        public void Subscribe<T, THandler>()
            where T : IntegrationEvent
            where THandler : IIntegrationEventHandler<T>
        {
            Task.Run(async () =>
            {
                // Retry subscription if channel is not ready
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    await EnsureConnectionAsync();
                    if (_channel != null)
                    {
                        break;
                    }
                    Console.WriteLine($"[RabbitMQEventBus] Subscribe for {typeof(T).Name} waiting for RabbitMQ channel (attempt {attempt + 1})...");
                    await Task.Delay(5000);
                }

                if (_channel == null)
                {
                    Console.WriteLine($"[RabbitMQEventBus] CRITICAL: Subscribe for {typeof(T).Name} failed. RabbitMQ channel is null.");
                    return;
                }

                var queueName = $"{typeof(T).Name}_{typeof(THandler).Name}_queue";
                await _channel.QueueDeclareAsync(queueName, durable: true, exclusive: false, autoDelete: false);
                await _channel.QueueBindAsync(queueName, _exchangeName, typeof(T).Name);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += async (model, ea) =>
                {
                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        var @event = JsonSerializer.Deserialize<T>(message);

                        if (@event != null)
                        {
                            using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_serviceProvider))
                            {
                                var handler = (THandler)scope.ServiceProvider.GetService(typeof(THandler)) 
                                              ?? (THandler)Activator.CreateInstance(typeof(THandler));
                                await handler.HandleAsync(@event);
                            }
                        }

                        await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error handling event {typeof(T).Name}: {ex.Message}");
                        // Nack and requeue
                        await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                };

                await _channel.BasicConsumeAsync(queueName, autoAck: false, consumer: consumer);
                Console.WriteLine($"[RabbitMQEventBus] Successfully subscribed to event: {typeof(T).Name}");
            });
        }
    }
}
