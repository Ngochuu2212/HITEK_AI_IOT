using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Hitek.IotService;

// Một lượt đọc đã chuẩn hoá: giá trị nhị phân, mốc thời gian UTC, kèm mốc nhận.
public sealed record NormalizedReading(Guid SensorId, bool Value, DateTime RecordedAt, DateTime ReceivedAt);

public sealed record NormalizeResult(NormalizedReading? Reading, string? Error, bool RecordedAtMissing = false)
{
    public bool IsValid => Reading is not null;
}

// Bốn bước chuẩn hoá trước khi dữ liệu đi tiếp (báo cáo thiết kế mục 2.5): kiểm tra hợp lệ,
// đưa thời gian về UTC, gắn mốc nhận, rồi mới chuyển sang Backend. Thiết bị thật không gửi
// đúng một kiểu: có loại gửi 0/1 thay vì true/false, có loại gửi epoch, có loại gửi giờ địa
// phương. Gom hết về một dạng ở đây để Risk Engine phía sau chỉ phải biết một dạng duy nhất.
public sealed partial class ReadingNormalizer(IOptions<IngestOptions> options)
{
    private static readonly DateTime EarliestAccepted = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Epoch lớn hơn mốc này chắc chắn là mili giây: 1e11 giây là năm 5138.
    private const long MillisecondsThreshold = 100_000_000_000;

    private readonly IngestOptions _opt = options.Value;
    private readonly TimeZoneInfo _deviceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.DeviceTimeZone);

    public NormalizeResult Normalize(Guid sensorId, ReadOnlyMemory<byte> payload, DateTime receivedAtUtc)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return Fail("Nội dung không phải JSON hợp lệ.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Fail("Nội dung phải là một đối tượng JSON dạng { \"value\": ..., \"recordedAt\": ... }.");

            if (!TryGetProperty(root, "value", out var valueElement))
                return Fail("Thiếu trường value.");
            if (!TryReadBinary(valueElement, out var value))
                return Fail($"value phải là giá trị nhị phân (true/false, 1/0, on/off), nhận được: {valueElement.GetRawText()}.");

            var missing = !TryGetProperty(root, "recordedAt", out var timeElement) || timeElement.ValueKind == JsonValueKind.Null;
            DateTime recordedAt;
            if (missing)
            {
                // Thiết bị không gửi mốc đo thì lấy mốc nhận: vẫn tốt hơn bỏ mất lượt đọc.
                recordedAt = receivedAtUtc;
            }
            else if (!TryReadTimestamp(timeElement, out recordedAt))
            {
                return Fail($"recordedAt không phải mốc thời gian hợp lệ (ISO 8601 hoặc epoch), nhận được: {timeElement.GetRawText()}.");
            }

            if (recordedAt < EarliestAccepted)
                return Fail($"recordedAt {recordedAt:O} quá xa trong quá khứ, nhiều khả năng đồng hồ thiết bị chưa được đặt.");
            if (recordedAt > receivedAtUtc.AddSeconds(_opt.MaxFutureSkewSeconds))
                return Fail($"recordedAt {recordedAt:O} ở tương lai quá {_opt.MaxFutureSkewSeconds} giây so với lúc nhận.");

            return new NormalizeResult(new NormalizedReading(sensorId, value, recordedAt, receivedAtUtc), null, missing);
        }

        static NormalizeResult Fail(string error) => new(null, error);
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadBinary(JsonElement element, out bool value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.Number when element.TryGetInt32(out var number) && number is 0 or 1:
                value = number == 1;
                return true;
            case JsonValueKind.String:
                switch (element.GetString()?.Trim().ToLowerInvariant())
                {
                    case "true" or "1" or "on" or "yes":
                        value = true;
                        return true;
                    case "false" or "0" or "off" or "no":
                        value = false;
                        return true;
                }
                break;
        }

        value = default;
        return false;
    }

    private bool TryReadTimestamp(JsonElement element, out DateTime utc)
    {
        utc = default;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt64(out var epoch):
                return TryFromEpoch(epoch, out utc);
            case JsonValueKind.String:
                var text = element.GetString()?.Trim() ?? string.Empty;
                if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var epochText))
                    return TryFromEpoch(epochText, out utc);
                return TryFromIso(text, out utc);
            default:
                return false;
        }
    }

    private static bool TryFromEpoch(long epoch, out DateTime utc)
    {
        try
        {
            utc = (epoch >= MillisecondsThreshold
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch)).UtcDateTime;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            utc = default;
            return false;
        }
    }

    private bool TryFromIso(string text, out DateTime utc)
    {
        utc = default;

        // Có ghi múi giờ (Z hoặc +07:00) thì tin theo múi giờ đó; không ghi thì áp múi giờ
        // cấu hình cho thiết bị.
        if (OffsetSuffix().IsMatch(text))
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
                return false;
            utc = withOffset.UtcDateTime;
            return true;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;
        utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _deviceTimeZone);
        return true;
    }

    // Múi giờ phải đứng ngay sau phần giờ, nếu không "2026-10-24" bị hiểu nhầm "-24" là múi giờ.
    [GeneratedRegex(@"\d{2}:\d{2}(:\d{2}(\.\d+)?)?\s*(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetSuffix();
}
