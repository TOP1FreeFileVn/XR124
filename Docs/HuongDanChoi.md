# XR124 – Hướng dẫn chơi (Meta Quest, VR)

Đấu trường triệu hồi: cắm kiếm xuống sàn để mở pháp trận gọi pet, sau đó kết ấn bằng tay (hoặc nút tay cầm) để ra lệnh cho pet đánh với quái AI.

## 1. Chuẩn bị kính

1. Bật **Developer Mode** cho Quest (app Meta Horizon trên điện thoại).
2. Trong kính: **Settings → Movement tracking → Hand and body tracking** → bật, và bật **Auto switch between hands and controllers** để chơi được bằng tay không.
3. Chơi đứng, dọn trống khoảng **2 m × 2 m**. Game dùng sàn thật làm mặt đấu trường (tracking theo mặt sàn), nên phải cúi xuống được để cắm kiếm.

## 2. Build lên kính

1. Cắm Quest vào máy tính bằng cáp USB‑C.
2. Lần đầu cắm: đeo kính, bấm **Allow USB debugging** và tick **Always allow from this computer**.
   Kiểm tra bằng `adb devices`: Quest phải hiện là `device`, không phải `unauthorized`.
3. Unity: **File → Build Profiles → Android** (project đã chuyển sẵn sang Android) → ở **Run Device** chọn đúng Quest (nếu máy có BlueStacks/emulator thì đừng chọn emulator) → **Build And Run**.

## 3. Luồng một trận

1. Vào game: **quái địch tự xuất hiện**, đứng chờ cách bạn khoảng 3 m (theo hướng bạn nhìn lúc game bắt đầu).
2. Mở **túi đồ trên cổ tay trái** → bấm **EQUIP** → kiếm triệu hồi hiện lơ lửng trước ngực.
3. Cầm kiếm, **đâm mũi kiếm thật nhanh xuống sàn**. Kiếm tự cắm đứng, **pháp trận** mở ra và **Zeru** nhảy lên, đáp đất, gầm rồi vào trận.
4. Pet tự di chuyển và đánh thường. Bạn **kết ấn** để ra chiêu.
5. Hết trận: **rút kiếm lên** → quái đứng lại chỗ cũ → cắm kiếm lần nữa để đánh tiếp.

Nếu cắm kiếm quá sát quái hoặc ra ngoài đấu trường, game từ chối và kiếm bay về; cắm lại chỗ trống hơn.

## 4. Điều khiển bằng tay (hand tracking)

| Việc | Thao tác |
|---|---|
| Mở/đóng túi đồ | Lật mu bàn tay trái lên (đồng hồ xanh ở cổ tay), **chạm đầu ngón trỏ phải vào mặt đồng hồ** |
| Bấm nút EQUIP / STORE | Chĩa tay phải vào nút (có tia sáng), **chụm ngón cái + ngón trỏ (pinch)** |
| Cầm kiếm | Đưa tay tới chuôi, **nắm cả bàn tay** hoặc **pinch** |
| Cắm kiếm | Đang nắm kiếm, **đâm nhanh xuống sàn** tới khi mũi chạm sàn |
| Ấn **A – Kiếm Chỉ** (tấn công) | Duỗi **ngón trỏ + ngón giữa**, gập các ngón còn lại, **chỉ vào quái** |
| Ấn **D – Thuẫn Chưởng** (phòng thủ) | **Xòe bàn tay**, lòng bàn tay hướng ra trước mặt |
| Ấn **H – Tâm Ấn** (hồi phục) | **Áp lòng bàn tay lên ngực**, lòng bàn tay hướng vào người |
| **Ultimate** | **Hai tay cùng làm Kiếm Chỉ**, giữ 0,5 giây (cần đủ năng lượng) |

Khi đang cầm kiếm, nhận diện ấn tạm tắt để tư thế cầm không bị hiểu nhầm thành ấn. Thả kiếm ra rồi mới kết ấn.

## 5. Điều khiển bằng tay cầm Touch

| Việc | Nút |
|---|---|
| Mở/đóng túi đồ | **Nhấn cần analog trái**, hoặc chạm tay cầm phải vào đồng hồ ở cổ tay trái |
| Bấm nút trong túi đồ | Chĩa tia tay cầm phải vào nút, bóp **cò (trigger)** |
| Cầm kiếm | Giữ **grip** (nút bên hông) ở chuôi kiếm |
| Ấn A / D / H | **A** / **B** / **X** |
| Ultimate | **Y** |

## 6. Kết ấn thành chiêu

- Làm **1–3 ấn liên tiếp**. Sau **1 giây** không làm thêm ấn (hoặc khi đủ 3 ấn) thì chiêu được thi triển. Thứ tự ấn không quan trọng.
- 1 ấn: **A**, **D**, **H**.
- 2 ấn: **AA**, **DD**, **HH**, **AD**, **AH**, **DH**.
- 3 ấn khác nhau: **ADH** (kỹ năng đặc biệt). Ba ấn có lặp (ví dụ AAD) là không hợp lệ.
- Mỗi chiêu tốn **AP** (tối đa 20, hồi 1 điểm mỗi 2 giây). AP tiêu hao được đổi thành **Năng lượng** để dùng Ultimate.
- Chi tiết sát thương, hệ và bảng khắc hệ: xem [GDD_ChienDau.pdf](GDD_ChienDau.pdf).

## 7. HUD

Trên đầu mỗi pet có **biểu tượng hệ + tên**, **thanh máu** (lớp trắng tụt chậm cho thấy lượng máu vừa mất, lớp xanh nhạt là khiên) và **thanh năng lượng** màu vàng.

## 8. Lỗi thường gặp

| Hiện tượng | Cách xử lý |
|---|---|
| Unity không thấy kính / `unauthorized` | Đeo kính bấm **Allow USB debugging**; rút cáp ra cắm lại nếu không thấy hộp thoại |
| Build chạy nhầm sang BlueStacks | Tắt BlueStacks hoặc chọn đúng Quest ở **Run Device** |
| Không thấy tay | Bật hand tracking trong Settings của kính; bỏ tay cầm xuống vài giây |
| Cắm kiếm không ăn | Đâm nhanh hơn và cho mũi kiếm chạm hẳn sàn; không cắm sát quái |
| Kết ấn không nhận | Thả kiếm trước; giữ tay trong tầm nhìn của kính; với Kiếm Chỉ phải chỉ vào quái |
