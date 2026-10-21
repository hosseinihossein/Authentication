using Confluent.Kafka;

namespace AspApp.Helpers;

public class KafkaProducerService
{
    public IProducer<Null, string> Producer { get; }

    public KafkaProducerService(IConfiguration configuration)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServer"],
            Acks = Acks.All,
        };

        Producer = new ProducerBuilder<Null, string>(config).Build();
    }
}