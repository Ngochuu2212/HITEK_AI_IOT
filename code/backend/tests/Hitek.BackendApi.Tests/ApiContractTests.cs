using System.Text.Json;
using Hitek.BackendApi.Auth;
using Hitek.BackendApi.Common;
using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Tests;

// Quy ước dữ liệu chung giữa các nhóm: mốc thời gian UTC, enum dạng chuỗi chữ hoa.
public class ApiContractTests
{
    private static readonly DateTime TenGioUtc = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-10-24T10:00:00Z")]
    [InlineData("2026-10-24T10:00:00.000Z")]
    [InlineData("2026-10-24T10:00:00+00:00")]
    [InlineData("2026-10-24T17:00:00+07:00")]
    [InlineData("2026-10-24T10:00:00")]
    public void MocThoiGian_KemHoacKhongKemMuiGio_DeuQuyVeMotMocUtc(string text)
    {
        Assert.True(UtcDateTimeParser.TryParse(text, out var utc));
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(TenGioUtc, utc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hom-qua")]
    [InlineData("2026-13-45T10:00:00Z")]
    public void ChuoiKhongPhaiMocThoiGian_ThiTuChoi(string text)
    {
        Assert.False(UtcDateTimeParser.TryParse(text, out _));
    }

    [Fact]
    public void GhiRaJson_LuonCoHauToZ_KeCaGiaTriDocTuDatabase()
    {
        // Npgsql và SQLite trả timestamp về dạng Unspecified; phía ngoài vẫn phải thấy "Z".
        var fromDatabase = DateTime.SpecifyKind(TenGioUtc, DateTimeKind.Unspecified);

        Assert.Equal("\"2026-10-24T10:00:00.0000000Z\"", JsonSerializer.Serialize(fromDatabase, Options()));
    }

    [Fact]
    public void Enum_GhiThanhChuoiChuHoa_DocKhongPhanBietHoaThuong()
    {
        var options = Options();

        Assert.Equal("\"CRITICAL\"", JsonSerializer.Serialize(AlertLevel.Critical, options));
        Assert.Equal("\"PIR\"", JsonSerializer.Serialize(EventSource.Pir, options));
        Assert.Equal(AlertHandledStatus.Acknowledged, JsonSerializer.Deserialize<AlertHandledStatus>("\"acknowledged\"", options));
    }

    [Fact]
    public void MatKhauDung_ThiKhop_SaiThiKhong()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Demo@123", workFactor: 4);
        var verifier = new PasswordVerifier();

        Assert.True(verifier.Verify("Demo@123", hash));
        Assert.False(verifier.Verify("demo@123", hash));
    }

    // Cột password_hash do AI Service quản lý; một dòng rỗng hay hỏng chỉ được làm đăng
    // nhập thất bại, không được làm sập request thành lỗi 500.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("khong-phai-hash-bcrypt")]
    [InlineData("$2a$10$ngan-qua")]
    [InlineData("$2")]
    public void HashRongHoacHong_ThiKhongKhop_VaKhongNemLoi(string? hash)
    {
        Assert.False(new PasswordVerifier().Verify("Demo@123", hash));
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions();
        ApiJson.Configure(options);
        return options;
    }
}
