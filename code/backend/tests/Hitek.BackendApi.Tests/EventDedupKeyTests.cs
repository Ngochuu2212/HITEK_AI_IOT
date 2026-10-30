using Hitek.BackendApi.Domain;

namespace Hitek.BackendApi.Tests;

// Khoá chống ghi trùng phải phân biệt được hai chuyện dễ lẫn: một lần gửi lại đúng
// cùng sự kiện, và hai lần phát hiện khác nhau xảy ra sát nhau. AI Service gửi mỗi
// khung hình một sự kiện nên gộp nhầm là mất dữ liệu thật.
public class EventDedupKeyTests
{
    private static readonly Guid Camera = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
    private static readonly DateTime At = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CungMaSuKienAi_CungMocThoiGian_ThiRaCungMotKhoa()
    {
        var aiEventId = Guid.NewGuid();

        // Một lần retry gửi lại đúng lần cập nhật cũ.
        Assert.Equal(EventDedupKey.ForAi(aiEventId, Camera, At, "person"), EventDedupKey.ForAi(aiEventId, Camera, At, "person"));
    }

    [Fact]
    public void CungMaSuKienAi_MocThoiGianMoi_ThiRaKhoaKhac()
    {
        var aiEventId = Guid.NewGuid();

        // Bên AI Service một dòng ai_event là cả một vụ vi phạm, cập nhật dần theo khung hình.
        // Lần cập nhật sau mang last_seen_at mới, gộp nó vào lần trước là Risk Engine tưởng
        // người đã rời khỏi vùng cấm trong khi người vẫn đứng đó.
        Assert.NotEqual(EventDedupKey.ForAi(aiEventId, Camera, At, "person"), EventDedupKey.ForAi(aiEventId, Camera, At.AddSeconds(2), "person"));
    }

    [Fact]
    public void KhongCoMaSuKien_NoiDungYHet_ThiRaCungMotKhoa()
    {
        var first = EventDedupKey.ForAi(null, Camera, At, "person");
        var second = EventDedupKey.ForAi(null, Camera, At, "person");

        Assert.Equal(first, second);
    }

    [Fact]
    public void KhongCoMaSuKien_LechMocThoiGian_ThiRaKhoaKhacNhau()
    {
        var first = EventDedupKey.ForAi(null, Camera, At, "person");
        var second = EventDedupKey.ForAi(null, Camera, At.AddMilliseconds(100), "person");

        // Đây là phép thử quan trọng nhất của nhóm này: mười sự kiện mỗi giây từ AI là
        // các sự kiện khác nhau thật, gộp lại là xoá mất dữ liệu hợp lệ.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void KhongCoMaSuKien_KhacCamera_ThiRaKhoaKhacNhau()
    {
        var first = EventDedupKey.ForAi(null, Camera, At, "person");
        var second = EventDedupKey.ForAi(null, Guid.NewGuid(), At, "person");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NguonIot_MuonMaBanGhiDoc_NenLuonKhacNhau()
    {
        var first = EventDedupKey.ForIot(Guid.NewGuid());
        var second = EventDedupKey.ForIot(Guid.NewGuid());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void KhoaCuaHaiNguonKhongTheLanNhau()
    {
        var shared = Guid.NewGuid();

        // Cùng một giá trị Guid nhưng đến từ hai nguồn khác nhau thì vẫn phải khác khoá,
        // nếu không một bản ghi đọc cảm biến có thể chặn mất một sự kiện AI.
        Assert.NotEqual(EventDedupKey.ForAi(shared, Camera, At, "person"), EventDedupKey.ForIot(shared));
    }
}
