using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace Hitek.IotService;

// Nghe toàn bộ cảm biến qua một lần đăng ký hitek/+/reading. Dùng managed client nên mất
// kết nối tới broker (mạng chập chờn, broker khởi động lại) thì tự kết nối lại và đăng ký
// lại, không phải đứng im mãi sau lần rớt mạng đầu tiên.
public sealed class MqttListener(
    IngestPipeline pipeline,
    IOptions<MqttOptions> mqttOptions,
    ILogger<MqttListener> logger) : BackgroundService
{
    private IManagedMqttClient? _client;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mqtt = mqttOptions.Value;
        if (!mqtt.Enabled)
        {
            logger.LogInformation("MQTT đang tắt (Mqtt:Enabled=false), chỉ nhận dữ liệu qua HTTP");
            return;
        }

        _client = new MqttFactory().CreateManagedMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.ConnectedAsync += _ =>
        {
            pipeline.Stats.MqttConnected = true;
            logger.LogInformation("Đã kết nối MQTT broker {Host}:{Port}", mqtt.Host, mqtt.Port);
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += args =>
        {
            if (pipeline.Stats.MqttConnected || args.Exception is not null)
                logger.LogWarning("Mất kết nối MQTT broker ({Reason}), sẽ tự kết nối lại", args.Exception?.Message ?? args.Reason.ToString());
            pipeline.Stats.MqttConnected = false;
            return Task.CompletedTask;
        };

        var options = new ManagedMqttClientOptionsBuilder()
            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
            .WithClientOptions(new MqttClientOptionsBuilder()
                .WithTcpServer(mqtt.Host, mqtt.Port)
                .WithClientId(mqtt.ClientId)
                .Build())
            .Build();

        // Phải chỉ định QoS ở phía nhận. Bỏ trống thì MQTTnet rơi về AtMostOnce, mà mức
        // thực tế lấy theo bên thấp hơn giữa hai đầu, nên dù bộ giả lập publish ở
        // AtLeastOnce thì đường truyền vẫn tụt về QoS 0 và cam kết không mất tin là hỏng.
        await _client.SubscribeAsync([
            new MqttTopicFilterBuilder()
                .WithTopic(mqtt.ReadingTopicFilter)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build()
        ]);
        await _client.StartAsync(options);
        logger.LogInformation("Đăng ký nhận {Topic} tại {Host}:{Port}", mqtt.ReadingTopicFilter, mqtt.Host, mqtt.Port);
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        // Topic dạng hitek/<sensorId>/reading: biết ngay cảm biến nào gửi, kể cả khi nội dung hỏng.
        var topic = args.ApplicationMessage.Topic;
        var segments = topic.Split('/');
        if (segments.Length != 3 || !Guid.TryParse(segments[1], out var sensorId))
        {
            logger.LogWarning("Bỏ qua tin trên topic không đúng quy ước: {Topic}", topic);
            return Task.CompletedTask;
        }

        pipeline.Accept(sensorId, args.ApplicationMessage.PayloadSegment.ToArray(), "mqtt");
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            await _client.StopAsync();
            _client.Dispose();
        }

        await base.StopAsync(cancellationToken);
    }
}
