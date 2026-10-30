using System.Text;
using Microsoft.Extensions.Options;

namespace Hitek.IotService.Tests;

// Bốn bước chuẩn hoá của IoT Service: kiểm tra hợp lệ, đưa thời gian về UTC, gắn mốc nhận.
public class ReadingNormalizerTests
{
    private static readonly Guid Sensor = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime ReceivedAt = new(2026, 10, 24, 10, 0, 5, DateTimeKind.Utc);
    private static readonly DateTime TenGioUtc = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    private static NormalizeResult Normalize(string json, string deviceTimeZone = "UTC") =>
        new ReadingNormalizer(Options.Create(new IngestOptions { DeviceTimeZone = deviceTimeZone }))
            .Normalize(Sensor, Encoding.UTF8.GetBytes(json), ReceivedAt);

    [Theory]
    [InlineData("""{"value": true,  "recordedAt": "2026-10-24T10:00:00Z"}""", true)]
    [InlineData("""{"value": false, "recordedAt": "2026-10-24T10:00:00Z"}""", false)]
    [InlineData("""{"value": 1,     "recordedAt": "2026-10-24T10:00:00Z"}""", true)]
    [InlineData("""{"value": 0,     "recordedAt": "2026-10-24T10:00:00Z"}""", false)]
    [InlineData("""{"value": "ON",  "recordedAt": "2026-10-24T10:00:00Z"}""", true)]
    [InlineData("""{"Value": "off", "RecordedAt": "2026-10-24T10:00:00Z"}""", false)]
    public void GiaTriNhiPhanNhieuKieu_DeuQuyVeTrueFalse(string json, bool expected)
    {
        var result = Normalize(json);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(expected, result.Reading!.Value);
    }

    [Theory]
    [InlineData("""{"value": true, "recordedAt": "2026-10-24T10:00:00Z"}""")]
    [InlineData("""{"value": true, "recordedAt": "2026-10-24T17:00:00+07:00"}""")]
    [InlineData("""{"value": true, "recordedAt": "2026-10-24T10:00:00.000+00:00"}""")]
    [InlineData("""{"value": true, "recordedAt": 1792836000}""")]
    [InlineData("""{"value": true, "recordedAt": 1792836000000}""")]
    [InlineData("""{"value": true, "recordedAt": "1792836000"}""")]
    [InlineData("""{"value": true, "recordedAt": "2026-10-24T10:00:00"}""")]
    public void MocThoiGianISOHoacEpoch_DeuQuyVeCungMotMocUtc(string json)
    {
        var result = Normalize(json);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(TenGioUtc, result.Reading!.RecordedAt);
        Assert.Equal(DateTimeKind.Utc, result.Reading.RecordedAt.Kind);
        Assert.Equal(ReceivedAt, result.Reading.ReceivedAt);
    }

    [Fact]
    public void GioDiaPhuongKhongKemMuiGio_ThiApMuiGioCauHinhChoThietBi()
    {
        var result = Normalize("""{"value": true, "recordedAt": "2026-10-24T17:00:00"}""", "Asia/Ho_Chi_Minh");

        Assert.Equal(TenGioUtc, result.Reading!.RecordedAt);
    }

    [Fact]
    public void ThieuRecordedAt_ThiLayMocNhan()
    {
        var result = Normalize("""{"value": true}""");

        Assert.True(result.IsValid);
        Assert.True(result.RecordedAtMissing);
        Assert.Equal(ReceivedAt, result.Reading!.RecordedAt);
    }

    [Theory]
    [InlineData("khong-phai-json")]
    [InlineData("""[true]""")]
    [InlineData("""{"recordedAt": "2026-10-24T10:00:00Z"}""")]
    [InlineData("""{"value": 2, "recordedAt": "2026-10-24T10:00:00Z"}""")]
    [InlineData("""{"value": "maybe", "recordedAt": "2026-10-24T10:00:00Z"}""")]
    [InlineData("""{"value": true, "recordedAt": "hom-qua"}""")]
    [InlineData("""{"value": true, "recordedAt": "1970-01-01T00:00:10Z"}""")]
    [InlineData("""{"value": true, "recordedAt": "2026-10-24T11:00:00Z"}""")]
    public void DuLieuSai_ThiTuChoiKemLyDo(string json)
    {
        var result = Normalize(json);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
