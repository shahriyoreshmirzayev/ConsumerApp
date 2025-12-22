using Confluent.Kafka;
using System.Text.Json;

namespace ConsumerApp;

public class FeedbackProducerService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FeedbackProducerService> _logger;
    private readonly string _bootstrapServers;
    private readonly string _feedbackTopic;

    public FeedbackProducerService(IConfiguration configuration, ILogger<FeedbackProducerService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _bootstrapServers = _configuration["Kafka:BootstrapServers"];
        _feedbackTopic = _configuration["Kafka:FeedbackTopic"];
    }

    public async Task<bool> SendFeedbackAsync(ApprovalFeedback feedback)
    {
        try
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true
            };

            using var producer = new ProducerBuilder<Null, string>(config).Build();

            var jsonMessage = JsonSerializer.Serialize(feedback, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            _logger.LogInformation("Yuborilmoqda: ", jsonMessage);

            var result = await producer.ProduceAsync(
                _feedbackTopic,
                new Message<Null, string>
                {
                    Value = jsonMessage,
                    Timestamp = new Timestamp(DateTime.UtcNow)
                }
            );

            _logger.LogInformation(
                "Yuborildi",
                feedback.ProductId,
                feedback.Status,
                result.Topic,
                result.Offset.Value
            );

            return true;
        }
        catch (ProduceException<Null, string> ex)
        {
            _logger.LogError(ex, "Xatolik", ex.Error.Reason);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Xatolik");
            return false;
        }
    }
}