# Web Dashboard

Giao diện web cho người vận hành, viết bằng React 18 + TypeScript (Vite). Dashboard đáp ứng mục 4 của đề án:

| Trang | Đường dẫn | Nội dung |
| --- | --- | --- |
| Tổng quan | `/` | Số cảnh báo đang mở, trạng thái camera và cảm biến, sự kiện hôm nay theo nguồn, cảnh báo hôm nay theo mức, cảnh báo gần nhất |
| Giám sát camera | `/cameras` | Ảnh camera kèm khung YOLO (người, ô tô, xe máy, mã theo dõi, độ tin cậy), vùng cấm, đối tượng trong vùng tô đỏ |
| Cảnh báo | `/alerts` | Lọc theo mức, trạng thái xử lý, khu vực, nguồn, loại sự kiện, thiết bị, thời gian |
| Chi tiết cảnh báo | `/alerts/:id` | Thông tin, cập nhật trạng thái xử lý và ghi chú, ảnh/clip bằng chứng, các sự kiện đã góp vào cảnh báo |
| Thiết bị | `/devices` | Camera và cảm biến, trạng thái trực tuyến, lần nhận dữ liệu cuối, lịch sử đọc của từng cảm biến. Quản trị viên thêm, sửa, ngưng camera (nguồn video mô phỏng, có tải video lên, hoặc camera IP RTSP; chọn độ chi tiết AI) |
| Tra cứu lịch sử | `/history` | Sự kiện camera AI và cảm biến, lọc theo nguồn, loại, khu vực, thiết bị, thời gian |
| Nhật ký hệ thống | `/logs` | Cảm biến mất kết nối/kết nối lại, cảnh báo phát sinh/đóng, thao tác người dùng (chỉ vận hành và quản trị) |

Cảnh báo mới hoặc vừa nâng mức hiện thành thông báo ở góc phải màn hình ngay khi Backend phát qua SignalR (`/hubs/alerts`). Bấm vào thông báo để mở chi tiết.

## Quyền theo vai trò

Đăng nhập bằng tài khoản Backend (`POST /api/v1/auth/login`). Tài khoản demo có mật khẩu `Demo@123`.

| Vai trò | Xem | Xử lý cảnh báo | Nhật ký |
| --- | --- | --- | --- |
| `owner`, `admin` | Có | Có | Có, thêm/sửa/ngưng camera |
| `operator` (`kho.operator@demo.local`) | Có | Có | Có |
| `viewer` (`cong.viewer@demo.local`) | Có | Không | Không |

Backend kiểm tra quyền ở mọi API; giao diện chỉ ẩn hoặc khoá các nút người dùng không được dùng.

## Nguồn dữ liệu

Trình duyệt chỉ gọi một địa chỉ. Nginx (trong Docker) hoặc Vite (khi dev) chuyển tiếp:

| Đường dẫn | Tới | Dùng cho |
| --- | --- | --- |
| `/api/v1/*`, `/hubs/*` | `backend:5250` | Cảnh báo, sự kiện, thiết bị, nhật ký, SignalR |
| `/zone/*` | `zone-service:8010` | Danh sách camera, vùng cấm, kết quả AI (SSE), ảnh khung hình, bằng chứng |
| `/ai/*` | `ai-service:8003` | Trạng thái AI Service |

Ảnh, SSE và video không gửi được header `Authorization`, nên dashboard gắn token Backend vào tham số `access_token`. Zone Service chấp nhận token này vì dùng chung `JWT_SECRET` với Backend.

Vùng cấm được vẽ ở Zone Editor của Zone Service (http://localhost:8010); dashboard chỉ hiển thị.

## Chạy

Trong Docker, service `web-dashboard` thuộc profile `backend`:

```powershell
docker compose up -d --build web-dashboard
# mở http://localhost:3000
```

Biến trong `.env`: `DASHBOARD_PORT` (mặc định 3000), `DASHBOARD_SHOW_DEMO_ACCOUNTS` (hiện tài khoản demo ở màn hình đăng nhập; đặt `false` khi trình diễn thật).

Khi sửa giao diện, chạy Vite ngoài Docker (các service khác vẫn chạy trong Docker):

```powershell
cd code\web_dashboard
npm install
npm run dev          # http://localhost:3000, tự tải lại khi sửa code
npm run build        # kiểm tra kiểu TypeScript và build bản tĩnh vào dist/
```

Nếu `web-dashboard` trong Docker đang chiếm cổng 3000, dừng nó trước: `docker compose stop web-dashboard`. Backend, Zone Service, AI Service ở địa chỉ khác thì đặt `BACKEND_URL`, `ZONE_URL`, `AI_URL` trước khi `npm run dev`.

## Cấu trúc

```text
src/
├── api/          # client.ts gọi API (token, lỗi), types.ts khớp DTO Backend và Zone Service
├── auth/         # đăng nhập, giữ token trong localStorage
├── realtime/     # kết nối SignalR, tự kết nối lại
├── components/   # khung trang, thông báo, badge, bảng phân trang
├── pages/        # mỗi trang một file
├── lib/format.ts # nhãn tiếng Việt, định dạng thời gian theo giờ máy người xem
└── styles.css    # giao diện sáng/tối theo hệ điều hành, co giãn tới màn hình điện thoại
```
