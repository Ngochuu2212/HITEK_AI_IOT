# HITEK AI-IoT: Phát hiện và cảnh báo bất thường

Prototype đề án thực tập HITEK SOLUTION: camera/cảm biến → AI phân tích → hợp nhất sự kiện → cảnh báo → lưu trữ và tra cứu.

> **Chạy lần đầu?** Đọc [docs/HUONG_DAN_CHAY.md](docs/HUONG_DAN_CHAY.md). Tóm tắt:
>
> ```powershell
> Copy-Item .env.example .env
> docker compose up -d --build
> # mở http://localhost:3000 (Web Dashboard), http://localhost:8010 (Zone Editor) và http://localhost:5250/swagger (Backend)
> ```
>
> Tài khoản demo (mật khẩu `Demo@123`): `owner@demo.local`, `admin@demo.local`, `kho.operator@demo.local`, `cong.viewer@demo.local`.

## Kiến trúc đang chạy

```text
video (code/data/videos) ─► camera1/camera2 (FFmpeg) ─RTSP─► mediamtx
   ─► edge-gateway :8002 ── frame (AI_FPS) ──► ai-service :8003  YOLOv11 → ByteTrack → Rule Engine
         │                                         │
         │ live video / frame theo frame_id        │ POST /api/ai-events
         ▼                                         ▼
   zone-service :8010  ◄─────────────────────────────  (ai_event, snapshot, clip ±5s)
   Dashboard + API  ──► postgres :5432 (metadata)  +  minio :9000 (ảnh / clip)
         │ POST /api/v1/events
         ▼
   backend :5250 (.NET)  Risk Engine kết hợp camera + cảm biến ──► risk_alert, SignalR /hubs/alerts
         ▲ POST /api/v1/iot/readings
   iot-service :5260 ◄── mosquitto :1883 ◄── sensor-simulator (PIR, Door, Beam)

   web-dashboard :3000 (React + Nginx) ──► /api/v1, /hubs → backend · /zone → zone-service · /ai → ai-service
```

| Service | Thư mục | Nhóm phụ trách |
| --- | --- | --- |
| `edge-gateway` | `code/ai_service/edge_gateway` | AI / Camera |
| `ai-service` | `code/ai_service` (`local_bbox_service.py`, `rule_engine/`) | AI / Camera |
| `zone-service` (backend + dashboard) | `code/zone_service` | Backend, Frontend |
| Database | `code/database/postgres/init` | Backend |
| `backend` (Backend API .NET 8: thiết bị, Event & Risk Engine, cảnh báo, người dùng, SignalR) | `code/backend/src/Hitek.BackendApi` | Backend & IoT |
| `iot-service` (nhận MQTT/HTTP, chuẩn hoá, chuyển sang Backend) | `code/backend/src/Hitek.IotService` | Backend & IoT |
| `sensor-simulator`, `mosquitto` (cảm biến giả lập PIR, Door, Beam) | `code/backend/src/Hitek.SensorSimulator` | Backend & IoT |
| `web-dashboard` (React: giám sát camera, cảnh báo realtime, thiết bị, tra cứu và xử lý cảnh báo) | `code/web_dashboard` | Frontend |

## Cấu trúc repo

```text
.
├── docker-compose.yml        # toàn bộ hệ thống
├── docker-compose.gpu.yml    # override khi máy có GPU NVIDIA
├── .env.example              # mẫu cấu hình, copy thành .env
├── code/
│   ├── ai_service/           # Edge Gateway + AI Service (Dockerfile multi-stage)
│   ├── zone_service/         # Zone Service FastAPI + dashboard (zone, AI event, bằng chứng)
│   ├── backend/              # Backend API, IoT Service, bộ giả lập cảm biến (.NET 8) + kiểm thử
│   ├── web_dashboard/        # Web Dashboard React cho người vận hành
│   ├── database/postgres/    # SQL khởi tạo schema + seed
│   └── data/                 # video/ảnh test (không commit video lớn)
└── docs/                     # hướng dẫn chạy, phân quyền, chạy AI trên GPU trường, nguồn dữ liệu test
```

## Tài liệu

- [docs/HUONG_DAN_CHAY.md](docs/HUONG_DAN_CHAY.md): cài đặt, các chế độ chạy, xử lý sự cố.
- [code/ai_service/README.md](code/ai_service/README.md): AI Service, Rule Engine, API `:8003`.
- [code/zone_service/README.md](code/zone_service/README.md): Zone Service và dashboard.
- [code/backend/README.md](code/backend/README.md): Backend API, IoT Service, Event & Risk Engine, danh sách API.
- [code/web_dashboard/README.md](code/web_dashboard/README.md): Web Dashboard, các trang và quyền theo vai trò.
- [code/database/postgres/README.md](code/database/postgres/README.md): schema database.
- [docs/PHAN_QUYEN.md](docs/PHAN_QUYEN.md): phân quyền theo tenant và khu vực.
