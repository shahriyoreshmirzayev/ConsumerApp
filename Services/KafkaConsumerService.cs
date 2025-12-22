using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ConsumerApp;

public class KafkaConsumerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly string _topic;
    private readonly string _bootstrapServers;
    private readonly string _groupId;

    public KafkaConsumerService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<KafkaConsumerService> logger)
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
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _bootstrapServers,
            GroupId = _groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,

            EnableAutoCommit = false,
            IsolationLevel = IsolationLevel.ReadCommitted,

            SessionTimeoutMs = 30000,
            MaxPollIntervalMs = 300000,

            FetchMinBytes = 1,
            FetchWaitMaxMs = 500
        };

        using var consumer = new ConsumerBuilder<Ignore, string>(config)
            .SetErrorHandler((_, e) =>
            {
                _logger.LogError("Consumer error", e.Reason);
            })
            .Build();

        consumer.Subscribe(_topic);

        _logger.LogInformation("Transactional Consumer ishga tushdi", _topic, _groupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(TimeSpan.FromSeconds(1));

                    if (consumeResult != null)
                    {
                        _logger.LogInformation("Message qabul qilindi",
                            consumeResult.Partition.Value, consumeResult.Offset.Value);

                        var success = await ProcessMessageWithTransaction(consumeResult.Message.Value);

                        if (success)
                        {
                            consumer.Commit(consumeResult);
                            _logger.LogInformation("Offset committed",
                                consumeResult.Partition.Value, consumeResult.Offset.Value);
                        }
                        else
                        {
                            _logger.LogWarning("Message processing failed");
                        }
                    }
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Consume xatolik", ex.Error.Reason);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Message processing xatolik");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Kafka Consumer to'xtatilmoqda...");
        }
        finally
        {
            try
            {
                consumer.Close();
                _logger.LogInformation("Kafka Consumer to'xtatildi");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Consumer close error");
            }
        }
    }

    private async Task<bool> ProcessMessageWithTransaction(string message)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            _logger.LogInformation("Database transaction started");

            var receivedProduct = JsonSerializer.Deserialize<ReceivedProduct>(message);

            if (receivedProduct == null)
            {
                _logger.LogWarning("Deserialize xatolik - message null");
                return false;
            }

            var exists = await context.ProductApprovals.AnyAsync(p => p.ProductId == receivedProduct.Id && p.Status == "Pending");

            if (exists)
            {
                _logger.LogInformation("Mahsulot allaqachon mavjud", receivedProduct.Id);
                await transaction.CommitAsync();
                return true;
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

            await transaction.CommitAsync();

            _logger.LogInformation("Transaction committed saved successfully", receivedProduct.Name, receivedProduct.Id);

            return true;
        }
        catch (JsonException ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "JSON parse xatolik, transaction rollback - Message: {Message}", message);
            return false;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Database save xatolik, transaction rollback");
            return false;
        }
    }
}