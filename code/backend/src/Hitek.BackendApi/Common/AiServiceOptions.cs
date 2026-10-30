namespace Hitek.BackendApi.Common;

public class AiServiceOptions
{
    // Địa chỉ zone-service của AI Service mà trình duyệt truy cập được. Dùng để dựng
    // đường dẫn tải ảnh/clip bằng chứng; bỏ trống thì EvidenceDto.MediaUrl trả null.
    public string? PublicBaseUrl { get; set; }
}
