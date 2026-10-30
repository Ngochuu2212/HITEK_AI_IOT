using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Hitek.IotService;

// Lấy lượt đọc từ hàng đợi, gửi sang POST /api/v1/iot/readings của Backend.
//
// Trước đây Backend lỗi là lượt đọc bị bỏ luôn (báo cáo thiết kế mục 2.8 ghi đây là hạn chế).
// Giờ lỗi tạm thời (mất kết nối, 5xx, 408, 429) thì giữ nguyên lượt đọc đó và thử lại với
// thời gian chờ tăng dần, các lượt sau nằm chờ trong hàng đợi theo đúng thứ tự. Lỗi do chính
// dữ liệu (400, 404 cảm biến không tồn tại) thì có thử lại cũng vô ích, ghi log rồi bỏ qua.
public sealed class BackendForwarder(
    IngestPipeline pipeline,
    BackendTokenProvider tokens,
    IHttpClientFactory httpClientFactory,
    IOptions<IngestOptions> options,
    TimeProvider clock,
    ILogger<BackendForwarder> logger) : BackgroundService
{
    public const string ClientName = "BackendApi";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Xin token ngay lúc khởi động để biết tenant của mình sớm và phát hiện sai cấu hình
        // client credentials từ đầu. Backend chưa lên thì thôi, lần gửi đầu tiên sẽ xin lại.
        try
        {
            await tokens.GetTokenAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Chưa xin được token từ Backend lúc khởi động: {Error}", ex.Message);
        }

        try
        {
            await foreach (var reading in pipeline.Reader.ReadAllAsync(stoppingToken))
                await ForwardAsync(reading, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Dừng service.
        }
    }

    private async Task ForwardAsync(NormalizedReading reading, CancellationToken ct)
    {
        var opt = options.Value;
        var delay = TimeSpan.FromMilliseconds(opt.RetryInitialDelayMilliseconds);
        var maxDelay = TimeSpan.FromSeconds(opt.RetryMaxDelaySeconds);

        while (true)
        {
            string failure;
            try
            {
                var client = httpClientFactory.CreateClient(ClientName);
                using var response = await client.PostAsJsonAsync("/api/v1/iot/readings", new
                {
                    sensorId = reading.SensorId,
                    value = reading.Value,
                    recordedAt = reading.RecordedAt,
                    receivedAt = reading.ReceivedAt
                }, Json, ct);

                if (response.IsSuccessStatusCode)
                {
                    pipeline.Stats.CountForwarded();
                    pipeline.Stats.LastForwardedAt = clock.GetUtcNow().UtcDateTime;
                    return;
                }

                var detail = await response.Content.ReadAsStringAsync(ct);
                if (!IsTransient(response.StatusCode))
                {
                    pipeline.Stats.CountRefusedByBackend();
                    logger.LogWarning("Backend từ chối lượt đọc của cảm biến {SensorId} ({Status}): {Detail}",
                        reading.SensorId, (int)response.StatusCode, detail);
                    return;
                }

                failure = $"HTTP {(int)response.StatusCode}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // TaskCanceledException ở đây là hết thời gian chờ của HttpClient, không phải dừng service.
                failure = ex.Message;
            }

            pipeline.Stats.CountRetry();
            pipeline.Stats.LastBackendError = $"{clock.GetUtcNow().UtcDateTime:O} {failure}";
            logger.LogWarning("Gửi sang Backend thất bại ({Failure}), thử lại sau {Delay}. Đang chờ trong hàng đợi: {Queued}",
                failure, delay, pipeline.QueueLength);

            await Task.Delay(delay, clock, ct);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, maxDelay.Ticks));
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
}
