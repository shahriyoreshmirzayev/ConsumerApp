using Confluent.Kafka;
using ConsumerApp.Data;
using ConsumerApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ConsumerApp.Services
{
    public class KafkaConsumerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<KafkaConsumerService> _logger;
        private readonly string _topic;
        private readonly string _bootstrapServers;
        private readonly string _groupId;

        public KafkaConsumerService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<KafkaConsumerService> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
            _topic = _configuration["Kafka:ConsumerTopic"];
            _bootstrapServers = _configuration["Kafka:BootstrapServers"];
            _groupId = _configuration["Kafka:GroupId"];
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield(); // Background task sifatida ishlash

            var config = new ConsumerConfig
            {
                BootstrapServers = _bootstrapServers,
                GroupId = _groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
            consumer.Subscribe(_topic);

            _logger.LogInformation("Kafka Consumer ishga tushdi - Topic: {Topic}", _topic);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = consumer.Consume(TimeSpan.FromSeconds(1));

                        if (consumeResult != null)
                        {
                            await ProcessMessage(consumeResult.Message.Value);
                            consumer.Commit(consumeResult);
                            _logger.LogInformation("Message processed - Offset: {Offset}", consumeResult.Offset);
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Consume xatolik: {Reason}", ex.Error.Reason);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Message processing xatolik");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Kafka Consumer to'xtatildi");
            }
            finally
            {
                consumer.Close();
            }
        }

        private async Task ProcessMessage(string message)
        {
            try
            {
                var receivedProduct = JsonSerializer.Deserialize<ReceivedProduct>(message);

                if (receivedProduct == null)
                {
                    _logger.LogWarning("Deserialize xatolik - message null");
                    return;
                }

                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Avval tekshirish - bir xil mahsulot qayta qo'shilmasin
                var exists = await context.ProductApprovals
                    .AnyAsync(p => p.ProductId == receivedProduct.Id && p.Status == "Pending");

                if (exists)
                {
                    _logger.LogInformation("Mahsulot allaqachon mavjud ", receivedProduct.Id);
                    return;
                }

                var approval = new ProductApproval
                {
                    ProductId = receivedProduct.Id,
                    ProductName = receivedProduct.Name,
                    Category = receivedProduct.Category,
                    Price = receivedProduct.Price,
                    Description = receivedProduct.Description,
                    Quantity = receivedProduct.Quantity,
                    Manufacturer = receivedProduct.Manufacturer,
                    KafkaMessage = message,
                    ReceivedDate = DateTime.UtcNow,
                    Status = "Pending"
                };

                context.ProductApprovals.Add(approval);
                await context.SaveChangesAsync();

                _logger.LogInformation("Yangi mahsulot qabul qilindi",
                    receivedProduct.Name, receivedProduct.Id);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parse xatolik:, message");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database save xatolik");
            }
        }
    }
}