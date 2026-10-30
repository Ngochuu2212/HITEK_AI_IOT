namespace Hitek.BackendApi.Dtos;

// Số liệu cho màn hình tổng quan (đề án mục 4.1). Dùng trường tường minh thay vì
// từ điển đếm theo khoá, để phía Dashboard biết trước đúng những trường sẽ nhận được
// và không phải đoán khi một mức nào đó chưa có bản ghi nào.
public record SensorSummaryDto(int Total, int Online, int Offline);

// Theo enum camera_status bên AI Service: active / inactive / offline / disabled.
public record CameraSummaryDto(int Total, int Active, int Inactive, int Offline, int Disabled);

public record EventSummaryDto(int Total, int Ai, int Pir, int Door, int Beam);

public record AlertSummaryDto(int Total, int Low, int Medium, int High, int Critical);

public record DashboardSummaryDto(
    CameraSummaryDto Cameras,
    SensorSummaryDto Sensors,
    EventSummaryDto EventsToday,
    AlertSummaryDto AlertsToday,
    int OpenAlerts,
    IReadOnlyList<AlertDto> RecentAlerts);
