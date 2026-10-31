namespace Hitek.BackendApi.Common;

public class CameraOptions
{
    // Thư mục chứa video mô phỏng camera (code/data/videos). Edge Gateway gắn đúng thư mục
    // này nên camera loại FILE chỉ lưu tên file. Đường dẫn tương đối tính từ thư mục chạy
    // Backend. Bỏ trống hoặc không tồn tại thì danh sách video rỗng.
    public string? VideoDirectory { get; set; }

    public static readonly string[] VideoExtensions = [".mp4", ".avi", ".mkv", ".mov", ".ts"];

    // Giới hạn dung lượng một video tải lên (MB). Đặt đồng bộ với client_max_body_size của Nginx.
    public const long MaxUploadBytes = 300L * 1024 * 1024;

    // imgsz YOLO cho phép chọn: 640 nhanh, 1280 cho người rất nhỏ (cảnh đông, quay từ xa).
    public static readonly int[] AiImageSizes = [640, 960, 1280];
}
