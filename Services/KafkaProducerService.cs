using Confluent.Kafka;
using ConsumerApp.Models;
using System.Text.Json;

namespace ConsumerApp.Services
{
    public class FeedbackProducerService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FeedbackProducerService> _logger;
        private readonly string _bootstrapServers;
        private readonly string _feedbackTopic;

        public FeedbackProducerService(
            IConfiguration configuration,
            ILogger<FeedbackProducerService> logger)
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

                var result = await producer.ProduceAsync(
                    _feedbackTopic,
                    new Message<Null, string>
                    {
                        Value = jsonMessage,
                        Timestamp = new Timestamp(DateTime.UtcNow)
                    }
                );

                _logger.LogInformation(
                    "✅ Feedback yuborildi - ProductId: {ProductId}, Status: {Status}, Topic: {Topic}, Offset: {Offset}",
                    feedback.ProductId,
                    feedback.Status,
                    result.Topic,
                    result.Offset.Value
                );

                return true;
            }
            catch (ProduceException<Null, string> ex)
            {
                _logger.LogError(ex, "❌ Feedback yuborishda xatolik - Reason: {Reason}", ex.Error.Reason);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Feedback xatolik");
                return false;
            }
        }
    }
}