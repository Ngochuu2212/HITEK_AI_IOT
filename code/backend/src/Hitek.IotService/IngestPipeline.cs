using System.Threading.Channels;

namespace Hitek.IotService;

// Số liệu vận hành, xem qua GET /api/v1/status.
public sealed class IngestStats
{
    private long _received;
    private long _rejected;
    private long _dropped;
    private long _forwarded;
    private long _refusedByBackend;
    private long _retries;

    public long Received => Interlocked.Read(ref _received);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Dropped => Interlocked.Read(ref _dropped);
    public long Forwarded => Interlocked.Read(ref _forwarded);
    public long RefusedByBackend => Interlocked.Read(ref _refusedByBackend);
    public long Retries => Interlocked.Read(ref _retries);

    public volatile bool MqttConnected;
    public DateTime? LastForwardedAt { get; set; }
    public string? LastBackendError { get; set; }

    public void CountReceived() => Interlocked.Increment(ref _received);
    public void CountRejected() => Interlocked.Increment(ref _rejected);
    public void CountDropped() => Interlocked.Increment(ref _dropped);
    public void CountForwarded() => Interlocked.Increment(ref _forwarded);
    public void CountRefusedByBackend() => Interlocked.Increment(ref _refusedByBackend);
    public void CountRetry() => Interlocked.Increment(ref _retries);
}

// Điểm vào chung của cả hai đường nhận dữ liệu. MQTT và HTTP dự phòng đều gọi Accept, nên
// không có chuyện hai đường chuẩn hoá lệch nhau theo thời gian (báo cáo thiết kế mục 2.2).
//
// Nhận xong chỉ chuẩn hoá rồi xếp hàng, không chờ Backend: Backend chậm hay tạm ngừng thì
// phía nhận tin vẫn chạy, dữ liệu nằm chờ trong hàng đợi thay vì bị bỏ như trước.
public sealed class IngestPipeline
{
    private readonly ReadingNormalizer _normalizer;
    private readonly TimeProvider _clock;
    private readonly ILogger<IngestPipeline> _logger;
    private readonly Channel<NormalizedReading> _queue;

    public IngestPipeline(
        ReadingNormalizer normalizer,
        Microsoft.Extensions.Options.IOptions<IngestOptions> options,
        IngestStats stats,
        TimeProvider clock,
        ILogger<IngestPipeline> logger)
    {
        _normalizer = normalizer;
        _clock = clock;
        _logger = logger;
        Stats = stats;
        _queue = Channel.CreateBounded<NormalizedReading>(
            new BoundedChannelOptions(options.Value.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            },
            dropped =>
            {
                Stats.CountDropped();
                _logger.LogWarning(
                    "Hàng đợi đầy, bỏ lượt đọc cũ nhất của cảm biến {SensorId} lúc {RecordedAt:O}",
                    dropped.SensorId, dropped.RecordedAt);
            });
    }

    public IngestStats Stats { get; }

    public ChannelReader<NormalizedReading> Reader => _queue.Reader;

    public int QueueLength => _queue.Reader.Count;

    public NormalizeResult Accept(Guid sensorId, ReadOnlyMemory<byte> payload, string channel)
    {
        Stats.CountReceived();
        var result = _normalizer.Normalize(sensorId, payload, _clock.GetUtcNow().UtcDateTime);

        if (!result.IsValid)
        {
            // Dữ liệu sai không đi tiếp vào hệ thống, chỉ ghi log để biết cảm biến nào gửi sai.
            Stats.CountRejected();
            _logger.LogWarning("Từ chối lượt đọc của cảm biến {SensorId} qua {Channel}: {Error}", sensorId, channel, result.Error);
            return result;
        }

        if (result.RecordedAtMissing)
            _logger.LogInformation("Cảm biến {SensorId} không gửi recordedAt, dùng mốc nhận thay thế", sensorId);

        _queue.Writer.TryWrite(result.Reading!);
        return result;
    }
}
