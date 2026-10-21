using System.Text.Json;
using Confluent.Kafka;

namespace AspApp.Helpers;

public class KafkaEmailSender
{
    private readonly KafkaProducerService _kafka;
    private readonly string _emailTopic;
    private readonly ILogger<KafkaEmailSender> _logger;


    public KafkaEmailSender(KafkaProducerService kafka, IConfiguration configuration,
    ILogger<KafkaEmailSender> logger)
    {
        _kafka = kafka;
        _emailTopic = configuration["Kafka:EmailTopic"] ?? "email";
        _logger = logger;
    }


    public void Send(EmailModel emailModel)
    {
        try
        {
            string messageJson = JsonSerializer.Serialize(emailModel);

            var message = new Message<Null, string>()
            {
                Value = messageJson,
            };

            _kafka.Producer.Produce(_emailTopic, message, (deliveryReport) =>
                {
                    if (deliveryReport.Error.Code != ErrorCode.NoError)
                    {
                        //Console.WriteLine($"Failed to deliver message: {deliveryReport.Error.Reason}");
                        _logger.LogWarning($"Failed to send email: {deliveryReport.Error.Reason}");
                    }
                    /*else 
                    {
                        Console.WriteLine($"Produced event to topic test: value = {messageJson}");
                    }*/
                }
            );
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Exception happened while sending email to kafka: {e.ToString()}");
        }
    }


}

public class EmailModel
{
    public string? ReceiverName { get; set; } = "User";
    public string ReceiverEmail { get; set; } = null!;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}