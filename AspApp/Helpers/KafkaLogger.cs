using System.Collections.Concurrent;
using Confluent.Kafka;

namespace AspApp.Helpers;

public class KafkaLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IProducer<Null, string> _kafkaProducer;
    private readonly string _topic;

    public KafkaLogger(string categoryName, IProducer<Null, string> kafkaProducer, string topic)
    {
        _categoryName = categoryName;
        _kafkaProducer = kafkaProducer;
        _topic = topic;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public async void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        // 1. Format the message
        var message = formatter(state, exception);

        // 2. Build a structured payload (JSON format is highly recommended for Kafka)
        string logPayload;
        if (exception is not null)
        {
            logPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                Timestamp = DateTime.UtcNow,
                LogLevel = logLevel.ToString(),
                Category = _categoryName,
                Message = message,
                Exception = exception?.ToString()
            });
        }
        else
        {
            logPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                Timestamp = DateTime.UtcNow,
                LogLevel = logLevel.ToString(),
                Category = _categoryName,
                Message = message,
                //Exception = exception?.ToString()
            });
        }

        try
        {
            // 3. Produce the message to the Kafka topic
            _kafkaProducer.Produce(_topic, new Message<Null, string> { Value = logPayload },
                (deliveryReport) =>
                {
                    if (deliveryReport.Error.Code != ErrorCode.NoError)
                    {
                        Console.WriteLine($"Failed to deliver message: {deliveryReport.Error.Reason}");
                    }
                    /*else {
                        Console.WriteLine($"Produced event to topic test: value = {messageJson}");
                    }*/
                }
            );
        }
        catch (Exception ex)
        {
            // Fallback to Console if Kafka publishing fails to avoid dropping critical errors silently
            Console.WriteLine($"[KafkaLogger Error] Failed to send log to Kafka: {ex.Message}");
        }
    }
}

[ProviderAlias("Kafka")]
public class KafkaLoggerProvider : ILoggerProvider
{
    private readonly string _topic;
    private readonly ConcurrentDictionary<string, KafkaLogger> _loggers = new();
    private readonly IProducer<Null, string> _kafkaProducer;

    public KafkaLoggerProvider(KafkaProducerService kafka, string topic)
    {
        //_configuration = configuration;
        _topic = topic;//configuration["KafkaLoggerTopic"] ?? "logger";
        _kafkaProducer = kafka.Producer;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, categoryName => new KafkaLogger(categoryName, _kafkaProducer, _topic));
    }

    public void Dispose()
    {
        // Important: Flush and dispose the producer to prevent log loss during app shutdown
        _kafkaProducer.Flush(TimeSpan.FromSeconds(5));
        _kafkaProducer.Dispose();
        _loggers.Clear();
    }
}

public static class KafkaLoggerExtensions
{
    public static ILoggingBuilder AddKafka(this ILoggingBuilder builder, KafkaProducerService kafka, IConfiguration configuration)
    {
        builder.AddProvider(new KafkaLoggerProvider(kafka, configuration["Kafka:LogTopic"] ?? "log"));
        return builder;
    }
}



