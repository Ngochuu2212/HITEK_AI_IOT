# HITEK AI-IoT — Backend API, IoT Service, Event & Risk Engine

Phần **Backend & IoT** của prototype AI-IoT phát hiện và cảnh báo bất thường (đề án `HITEK_De_an_thuc_tap_AI_IoT_Canh_bao_bat_thuong_v1.1`). Người phụ trách: Phạm Ngọc Hữu.

Phần này nhận dữ liệu cảm biến (MQTT, HTTP dự phòng) và sự kiện từ AI Service, gộp nguồn theo bảng luật để quyết định mức cảnh báo, lưu trữ, rồi cung cấp REST API và kênh realtime SignalR cho Web Dashboard.

## Cấu trúc

```
src/Hitek.BackendApi       .NET 8 Web API: REST /api/v1, SignalR, Event & Risk Engine, EF Core migration
src/Hitek.IotService       Cầu nối MQTT/HTTP → Backend: chuẩn hoá, hàng đợi, thử lại. Không chạm database
src/Hitek.SensorSimulator  Giả lập 3 cảm biến PIR, Door, Beam qua MQTT
tests/                     Kiểm thử đơn vị và tích hợp (xUnit) cho Backend và IoT Service
Dockerfile                 Build Backend API cho compose chung (BACKEND_PROJECT, BACKEND_DLL trong .env)
mosquitto/config/          Cấu hình MQTT broker dùng trong compose chung
```

Luồng dữ liệu:

```
Cảm biến ──MQTT──► IoT Service ──HTTP──► Backend API ──► PostgreSQL (dùng chung)
Thiết bị ──HTTP──┘   (chuẩn hoá,          │  Event & Risk Engine
                      hàng đợi)           ├─► SignalR /hubs/alerts ──► Web Dashboard
AI Service ──────── POST /api/v1/events ──┘   REST /api/v1/*      ──► Web Dashboard
```

## Chạy bằng Docker

Thư mục này nằm trong repo chung của dự án. Chạy toàn hệ thống từ thư mục gốc (xem `docs/HUONG_DAN_CHAY.md`):

```
Copy-Item .env.example .env
docker compose up -d --build
```

Profile `backend` trong compose chung gồm `backend` (cổng 5250), `iot-service` (5260), `mosquitto` (1883) và `sensor-simulator`. Muốn trình diễn cảnh báo đa nguồn lên Critical: đặt `SIMULATOR_MODE=intrusion` trong `.env` rồi `docker compose up -d sensor-simulator`.

Ở môi trường Development, Backend tự chạy migration khi khởi động (`Database:MigrateOnStartup`) và đặt mật khẩu `Demo@123` cho các tài khoản demo chưa có mật khẩu (`DevSeed:DefaultPassword`). Môi trường khác không chạy hai bước này.

Tài khoản demo (do script init của AI Service tạo, chỉ dùng cho dev), mật khẩu đều là `Demo@123`:

| Email | Vai trò | Ghi chú |
|---|---|---|
| owner@demo.local | owner | |
| admin@demo.local | admin | |
| kho.operator@demo.local | operator | Bên Zone Service chỉ cho xem khu Warehouse |
| cong.viewer@demo.local | viewer | Bên Zone Service chỉ cho xem khu Main Gate |

Client của service nội bộ (`appsettings.Development.json` của Backend): `iot-service` / `iot-service-dev-secret`, `ai-service` / `ai-service-dev-secret`. Khoá ký JWT lấy từ biến `JWT_SECRET` trong `.env` (compose truyền thành `Jwt__SigningKey`).

## Chạy tay khi phát triển

```
docker compose up -d postgres mosquitto      # chạy ở thư mục gốc repo
dotnet run --project src/Hitek.BackendApi          # http://localhost:5250, tự migrate ở Development
dotnet run --project src/Hitek.IotService          # http://localhost:5260
dotnet run --project src/Hitek.SensorSimulator -- localhost 1883 random
dotnet test HitekAiIot.slnx
```

`appsettings.json` trỏ database tới `localhost:5433`; nếu `.env` để `POSTGRES_PORT=5432` thì sửa chuỗi kết nối hoặc đặt biến `ConnectionStrings__Default` khi chạy.

## Quy ước chung

- REST JSON dưới `/api/v1`, tên trường camelCase.
- Mốc thời gian ISO 8601 UTC, luôn trả về có hậu tố `Z`. Gửi lên chấp nhận cả `Z` lẫn offset (`+07:00`), Backend tự quy về UTC. Trên query string nhớ mã hoá dấu `+` thành `%2B`.
- Id là UUID dạng chuỗi.
- Enum trả về chữ hoa có gạch dưới: `CRITICAL`, `PIR`, `ACKNOWLEDGED`, `SENSOR`. Gửi lên không phân biệt hoa thường.
- Lỗi luôn có dạng `{ "error": { "code": "...", "message": "..." } }`, mã HTTP 200/201/202/204/400/401/403/404/409/500.
- Hai endpoint nhận dữ liệu (`/api/v1/iot/readings`, `/api/v1/events`) trả 202: đã tiếp nhận, gửi lại trùng cũng vẫn 202.

## Xác thực và phân quyền

Mọi endpoint (trừ `/health`, đăng nhập, xin token) yêu cầu header `Authorization: Bearer <token>`.

- Người dùng Dashboard: `POST /api/v1/auth/login` với `{ email, password }`. Ai thuộc nhiều tenant thì gửi thêm `tenantId`.
- Service nội bộ: `POST /api/v1/auth/token` với `{ clientId, clientSecret }`.
- Tenant hiện tại chỉ lấy từ claim `tenant_id` trong token, không lấy từ header hay tham số nào.

| Vai trò | Quyền |
|---|---|
| viewer | Xem cảnh báo, sự kiện, thiết bị, tổng quan, bảng luật |
| operator | Như viewer, thêm cập nhật xử lý cảnh báo, xem nhật ký hệ thống |
| admin, owner | Như operator, thêm quản lý cảm biến, bảng luật, người dùng |
| service | Gửi dữ liệu cảm biến và sự kiện AI, đọc danh sách khu vực. Không đọc được dữ liệu cảnh báo |

Khoá ký JWT của môi trường thật đặt qua biến môi trường `Jwt__SigningKey` (tối thiểu 32 byte); thiếu khoá thì Backend không khởi động.

## Danh sách API

| Method | Đường dẫn | Quyền | Mô tả |
|---|---|---|---|
| POST | /api/v1/auth/login | — | Đăng nhập người dùng |
| POST | /api/v1/auth/token | — | Token cho service nội bộ |
| GET | /api/v1/auth/me | đã đăng nhập | Thông tin token hiện tại |
| PUT | /api/v1/auth/password | người dùng | Tự đổi mật khẩu |
| POST | /api/v1/iot/readings | service | Ghi một lượt đọc cảm biến đã chuẩn hoá |
| POST | /api/v1/events | service | Ghi sự kiện AI, trả `{ alertId }` |
| GET | /api/v1/events | viewer | Lịch sử sự kiện, lọc `source`, `eventType`, `areaId`, `deviceId`, `from`, `to` |
| GET | /api/v1/alerts | viewer | Danh sách cảnh báo, lọc `level`, `status`, `open`, `areaId`, `source`, `eventType`, `deviceId`, `from`, `to` |
| GET | /api/v1/alerts/{id} | viewer | Chi tiết cảnh báo |
| GET | /api/v1/alerts/{id}/events | viewer | Truy vết: sự kiện nào đẩy cảnh báo lên mức nào |
| GET | /api/v1/alerts/{id}/evidence | viewer | Ảnh/clip bằng chứng, kèm `mediaUrl` sang AI Service |
| PATCH | /api/v1/alerts/{id} | operator | Cập nhật trạng thái xử lý và ghi chú |
| GET | /api/v1/dashboard/summary | viewer | Số camera/cảm biến theo trạng thái, sự kiện và cảnh báo trong ngày, cảnh báo gần nhất |
| GET | /api/v1/devices | viewer | Camera và cảm biến chung một bảng, lọc `kind`, `areaId` |
| GET | /api/v1/areas | viewer, service | Khu vực của tenant |
| GET | /api/v1/sensors | viewer | Danh sách cảm biến |
| GET | /api/v1/sensors/{id} | viewer | Chi tiết cảm biến |
| GET | /api/v1/sensors/{id}/readings | viewer | Lịch sử đọc thô, lọc `from`, `to` |
| POST, PUT, DELETE | /api/v1/sensors[/{id}] | admin | Thêm, sửa tên/khu vực, gỡ cảm biến |
| GET | /api/v1/cameras[/{id}] | admin | Camera kèm nguồn (`sourceUrl` có thể chứa mật khẩu camera nên chỉ admin xem) |
| GET | /api/v1/cameras/video-files | admin | Video mô phỏng chọn được (thư mục `Cameras:VideoDirectory`), kèm camera đang dùng |
| POST | /api/v1/cameras/video-files | admin | Tải video lên (multipart, trường `file`, tối đa 300 MB): kiểm tra đuôi và chữ ký file, bỏ dấu tên, không ghi đè |
| POST, PUT | /api/v1/cameras[/{id}] | admin | Thêm, sửa camera: tên, khu vực, nguồn `RTSP` (rtsp/rtsps/http/https) hoặc `FILE` (tên video), `aiImageSize` 640/960/1280 (độ chi tiết YOLO). Edge Gateway tự áp dụng trong vài giây |
| PATCH | /api/v1/cameras/{id}/status | admin | `{ "enabled": false }` ngưng camera (giữ lịch sử), `true` bật lại |
| GET | /api/v1/risk-rules | viewer | Bảng luật cảnh báo của tenant |
| POST, PUT, DELETE | /api/v1/risk-rules[/{id}] | admin | Thêm, sửa, xoá luật |
| GET | /api/v1/system-logs | operator | Nhật ký hệ thống, lọc `category`, `level`, `entityId`, `from`, `to` |
| GET, POST | /api/v1/users | admin | Danh sách và thêm thành viên tenant |
| PATCH | /api/v1/users/{userId} | admin | Đổi vai trò, khoá/mở thành viên |
| POST | /api/v1/users/{userId}/reset-password | admin | Đặt lại mật khẩu (tài khoản chỉ thuộc tenant này) |
| — | /hubs/alerts | viewer | SignalR, sự kiện `AlertUpdated` |
| GET | /health | — | Kiểm tra sống và kết nối database |

Các danh sách có phân trang `page`, `pageSize` (tối đa 100), trả `{ items, page, pageSize, total }`. Chi tiết từng trường xem Swagger.

## IoT Service

- Nhận MQTT trên topic `hitek/<sensorId>/reading`, QoS 1. Mất kết nối broker thì tự kết nối và đăng ký lại.
- Đường HTTP dự phòng: `POST /api/v1/readings/{sensorId}` (cần token service), cùng nội dung với tin MQTT. Cả hai đường đi qua đúng một hàm xử lý.
- Nội dung tin: `{ "value": true, "recordedAt": "2026-10-24T10:00:00Z" }`.
- Chuẩn hoá trước khi gửi đi:
  - `value` nhận true/false, 1/0, "on"/"off".
  - `recordedAt` nhận ISO 8601 có hoặc không có múi giờ (không có thì áp `Ingest:DeviceTimeZone`), epoch giây hoặc mili giây; thiếu thì lấy mốc nhận.
  - Từ chối mốc ở tương lai quá 5 phút hoặc trước năm 2000; dữ liệu sai được ghi log, không đi vào hệ thống.
  - Gắn `receivedAt` để đo độ trễ đường truyền.
- Gửi sang Backend qua hàng đợi trong bộ nhớ (mặc định 1000 lượt, đầy thì bỏ lượt cũ nhất). Backend lỗi tạm thời thì giữ lượt đọc và thử lại với thời gian chờ tăng dần tới 30 giây; Backend báo dữ liệu sai (404 cảm biến không tồn tại) thì bỏ qua.
- Tự xin token service và xin lại khi hết hạn hoặc gặp 401.
- `GET /api/v1/status` (token service): trạng thái MQTT, độ dài hàng đợi, số lượt nhận/gửi/từ chối/thử lại. `GET /health` không cần token.

## Event & Risk Engine

Chạy ngay sau mỗi lượt đọc hoặc sự kiện AI được ghi xuống, và chạy lại định kỳ cho các khu vực đang có cảnh báo mở (để đóng cảnh báo khi mọi thiết bị đã im lặng).

- **Nguồn đang hoạt động**: cảm biến có 3 lượt đọc gần nhất đều true và đang ONLINE; AI có sự kiện độ tin cậy ≥ 0.6 trong 3 giây gần nhất. Nhiều cảm biến cùng loại trong một khu vực tính là một nguồn.
- **Bảng luật** (đối tượng RiskRule, mỗi tenant một bộ, sửa qua API, có hiệu lực ngay):

| Mã | Điều kiện | Mức |
|---|---|---|
| R1-1 | 1 nguồn | MEDIUM |
| R1-2 | 2 nguồn | HIGH |
| R1-3 | từ 3 nguồn | CRITICAL |
| R2 | từ 2 nguồn, ngoài khung giờ cho phép (mặc định 01:00–11:00 UTC, tức 08:00–18:00 giờ Việt Nam) | CRITICAL |
| R3 | AI và Door cùng hoạt động | CRITICAL |

  Mức cảnh báo là mức cao nhất trong các luật đang khớp; `triggeredRules` của cảnh báo ghi mã luật đã quyết định mức đó.
- **Gom cảnh báo**: mỗi khu vực chỉ có một cảnh báo mở. Hết nguồn hoạt động thì chờ 60 giây không còn tín hiệu mới đóng, để một nhiễu lẻ không làm cảnh báo chớp tắt.
- **Mức**: `level` là mức hiện tại (về LOW khi đóng), `peakLevel` là mức cao nhất từng đạt; lọc lịch sử theo `level` là lọc theo mức cao nhất.
- **Truy vết**: bảng `alert_event` ghi sự kiện nào góp vào cảnh báo và ở mức nào.
- **Chống ghi trùng**: lượt đọc trùng (sensor, recordedAt) chỉ ghi một lần. Sự kiện AI: cùng `aiEventId` và cùng `occurredAt` là gửi lại; cùng vụ vi phạm mà cập nhật mới thì ghi thêm, tối đa một dòng mỗi giây.
- **Loại sự kiện**: AI lấy `ai_event.event_type` (person_intrusion, loitering, crowd_limit...); cảm biến là motion_detected, door_opened, beam_broken.

Tham số kỹ thuật dùng chung nằm ở mục `RiskEngine` trong `appsettings.json` (`DebounceCount`, `AiActiveWindowSeconds`, `MinConfidence`, `CooldownSeconds`, `AiEventMinIntervalSeconds`); ngưỡng mất kết nối ở mục `SensorMonitor` (quá 30 giây không có tin là OFFLINE, quét mỗi 10 giây).

## Realtime

Kết nối `/hubs/alerts` bằng client SignalR, gửi token qua `accessTokenFactory` (trình duyệt gửi dạng `?access_token=`). Mỗi kết nối chỉ nhận cảnh báo của tenant trong token. Sự kiện `AlertUpdated` mang đúng hình dạng như `GET /api/v1/alerts/{id}`, phát khi cảnh báo được tạo, đổi mức, đổi trạng thái xử lý hoặc đóng — không phát ở mỗi lượt đọc.

## Dữ liệu

Dùng chung một PostgreSQL với AI Service; mỗi bảng chỉ một bên được tạo và đổi cấu trúc.

| Bên | Bảng | Công cụ |
|---|---|---|
| AI Service | tenant, app_user, tenant_member, location, user_location_access, camera, camera_reference_frame, zone, rule_config, ai_event, evidence | Script SQL init |
| Backend | sensor, sensor_reading, event, risk_alert, alert_event, risk_rule, system_log | EF Core migration (`src/Hitek.BackendApi/Data/Migrations`) |

Backend đọc camera, ai_event, evidence; ghi dữ liệu (không đổi cấu trúc) vào app_user và tenant_member qua API quản lý người dùng.

Cảnh báo chính thức của Backend nằm ở bảng `risk_alert`. Bảng `alert` vẫn do script init của AI Service tạo và chỉ đóng vai trò bảng ánh xạ: khi nhận `alertId` từ Backend, Zone Service ghi một dòng `dedup_key = backend:<alertId>` vào đó để giữ khoá ngoại của `ai_event.alert_id` và `evidence.alert_id`. Hai bảng cùng id nên tra bằng chứng theo `alertId` vẫn khớp.

## Kiểm thử

`dotnet test HitekAiIot.slnx` — 138 phép thử:

- Risk Engine trên SQLite (khoá ngoại, ràng buộc duy nhất thật): chống nhiễu, từng luật R1–R3, luật tuỳ chỉnh, vòng đời cảnh báo, truy vết, cách ly tenant, phát hiện mất kết nối.
- Tích hợp qua HTTP thật (WebApplicationFactory): đăng nhập, token hết hạn, token giả, phân quyền theo vai trò, nhận dữ liệu IoT và AI tới cảnh báo, mốc thời gian có offset, chống trùng, bằng chứng, xử lý cảnh báo, bảng luật, nhật ký, người dùng, cảm biến, SignalR chỉ đẩy đúng tenant, CORS, Swagger.
- IoT Service: chuẩn hoá dữ liệu nhiều định dạng, đường HTTP, thử lại khi Backend lỗi, xin lại token khi gặp 401.

Ngoài ra đã chạy toàn bộ hệ thống bằng Docker với PostgreSQL thật: luồng MQTT → IoT → Backend, cảnh báo lên Critical theo kịch bản đột nhập, cảm biến mất kết nối và tự đóng cảnh báo, Backend ngừng giữa chừng mà IoT Service không mất lượt đọc nào. Luồng AI → Backend đã chạy thử với chính Zone Service của AI Service (`ALERT_MODE=backend`): ghi `ai_event`, gọi `POST /api/v1/events`, nhận `alertId`, ghi dòng ánh xạ, heartbeat mỗi giây được ghi thành sự kiện; token Backend dùng được ở Zone Service khi `AUTH_MODE=jwt`.
