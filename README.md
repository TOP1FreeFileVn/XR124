# XR124 – Đấu trường triệu hồi (VR · Meta Quest)

Game VR đấu pet trên đấu trường ảo giữa biển mây. Người chơi **cắm thanh kiếm triệu hồi xuống sàn** để mở **pháp trận** gọi pet. Pet **tự di chuyển và đánh thường**, còn người chơi **kết ấn bằng tay** (hoặc bấm nút tay cầm) để ra chiêu và kích hoạt Ultimate.

- Nền tảng: **Meta Quest** (VR, không dùng passthrough), chơi được bằng **tay không (hand tracking)** hoặc **tay cầm Touch**
- Engine: **Unity 6000.0.78f1**, URP, OpenXR + Meta XR SDK (Core, Interaction SDK)
- Tài liệu thiết kế: [Docs/GDD_ChienDau.pdf](Docs/GDD_ChienDau.pdf) · Hướng dẫn chơi rút gọn: [Docs/HuongDanChoi.md](Docs/HuongDanChoi.md)

---

## Mục lục

1. [Tổng quan gameplay](#1-tổng-quan-gameplay)
2. [Pet](#2-pet)
3. [Hệ thống chiến đấu](#3-hệ-thống-chiến-đấu)
4. [Hướng dẫn chơi](#4-hướng-dẫn-chơi)
5. [Cài đặt & build lên kính](#5-cài-đặt--build-lên-kính)
6. [Cấu trúc project](#6-cấu-trúc-project)
7. [Công cụ trong Editor](#7-công-cụ-trong-editor)
8. [Lỗi thường gặp](#8-lỗi-thường-gặp)

---

## 1. Tổng quan gameplay

```
Vào game ─► Quái địch tự xuất hiện (đứng chờ cách bạn ~3 m)
        ─► Mở túi đồ ở cổ tay trái ─► EQUIP kiếm
        ─► Đâm kiếm xuống sàn ─► Pháp trận mở ─► Pet nhảy lên, đáp đất, gầm
        ─► Trận đấu: pet tự đánh, bạn kết ấn A / D / H để ra chiêu, hai tay Kiếm Chỉ để Ultimate
        ─► Hết trận ─► Rút kiếm ─► Quái đứng lại ─► Cắm kiếm để đánh trận mới
```

Điểm nổi bật:

- **Cắm kiếm triệu hồi:** SummonSword dùng Interaction SDK. Kiếm nhận ra cú đâm xuống khi mũi kiếm chạm sàn đủ nhanh, rồi tự cắm đứng.
- **Pháp trận viết bằng HLSL:** shader `XR124/MagicCircle` vẽ các vòng, rune xoay, ngôi sao sáu cánh và lõi đập nhịp hoàn toàn bằng toán, không dùng texture. Pháp trận "vẽ dần" từ tâm ra khi mở và hỗ trợ render stereo trên Quest.
- **Kết ấn bằng tay thật:** đọc khung xương tay `OVRSkeleton` (`XRHand_*`) để nhận diện tư thế ngón tay và hướng lòng bàn tay.
- **Pet có animation đầy đủ:** idle, đi, chạy, đánh, trúng đòn, chết, nhảy ra khỏi pháp trận, rơi, đáp đất, gầm.
- **HUD trên đầu pet:** biểu tượng hệ, tên, thanh máu (lớp trễ và khiên) và thanh năng lượng.

## 2. Pet

| Pet | Vai trò | Hệ | HP | ATK | DEF | SPD | Tầm đánh | Ultimate |
|---|---|---|---|---|---|---|---|---|
| **Zeru** | Pet người chơi | Thần thoại (chọn được hệ trước trận) | 1350 | 150 | 100 | 105 | 1.5 m | 17 năng lượng |
| **Onea** | Quái AI mặc định | Thần thoại | 1500 | 140 | 110 | 95 | 1.2 m | 15 năng lượng |
| **Tiwo** | Rồng lửa (model mới) | Lửa | 1280 | 165 | 88 | 115 | 1.8 m | 9 năng lượng |

**Kỹ năng riêng** (Skill = combo A‑D‑H):

- **Zeru:**
  - Skill: cường hóa 6 giây, giảm hồi chiêu của A và A‑A, đồng thời ghi lại 40% sát thương gây ra (tối đa 200% ATK).
  - Ultimate: phóng vòng năng lượng gây 50% ATK cộng 75% lượng sát thương đã ghi.
- **Onea:**
  - Skill: tụ lực 3 giây (giảm sát thương nhận vào) rồi đánh 150% ATK.
  - Ultimate: tạo lãnh địa 5 giây; đứng trong lãnh địa thì Onea +20% ATK, còn đối thủ chịu thêm sát thương.
  - Passive: càng mất máu càng mạnh.
- **Tiwo:**
  - Skill: gây Thiêu đốt; nếu mục tiêu đang cháy thì kích nổ ngay phần sát thương còn lại.
  - Ultimate: cho Thiêu đốt cộng dồn tới 3 tầng.
  - Passive: +20% sát thương và bỏ qua 30% DEF của mục tiêu đang cháy.

**8 hệ:** Lửa, Cỏ, Nước, Điện, Băng, Đất, Thường, Thần thoại. Mỗi cặp hệ có hệ số khắc chế (xem GDD).

## 3. Hệ thống chiến đấu

- **AP:** tối đa 20, hồi 1 điểm mỗi 2 giây. Mỗi chiêu tốn AP; AP đã tiêu tích thành **Năng lượng** để dùng Ultimate.
- **Phòng thủ:** sát thương nhận vào × `K / (K + DEF)`, với `K = 300`.
- **Pet tự đánh thường** khi đối thủ vào tầm. Kết ấn là để ra lệnh dùng chiêu.

### Ba ấn cơ bản

| Ấn | Tên | Ý nghĩa |
|---|---|---|
| **A** | Kiếm Chỉ | Tấn công |
| **D** | Thuẫn Chưởng | Phòng thủ |
| **H** | Tâm Ấn | Hồi phục |

### Bảng chiêu (ghép 1–3 ấn, thứ tự không quan trọng)

| Chiêu | AP | Hồi chiêu | Hiệu ứng |
|---|---|---|---|
| **A** | 1 | 3 s | Đánh 50% ATK |
| **D** | 1 | 3 s | Giảm 10% sát thương nhận trong 1.5 s |
| **H** | 1 | 3 s | Hồi 15% HP tối đa |
| **AA** | 4 | 5 s | Đánh 100% ATK + hiệu ứng theo hệ |
| **DD** | 4 | 5 s | Giảm 25% sát thương nhận trong 2 s |
| **HH** | 4 | 5 s | Hồi 25% HP tối đa + hoàn 2 AP |
| **AD** | 4 | 5 s | Đánh 75% ATK, tạo khiên bằng 50% sát thương gây ra |
| **AH** | 4 | 5 s | Đánh 75% ATK, hút máu 50% |
| **DH** | 4 | 5 s | Khiên bằng 25% HP tối đa |
| **ADH** | 9 | 10 s | Kỹ năng riêng của pet (Skill) |
| **Ultimate** | – | – | Tốn năng lượng (theo pet) |

Ghép ấn: làm 1–3 ấn liên tiếp; sau **1 giây** không làm thêm ấn (hoặc khi đủ 3 ấn) thì chiêu được thi triển. Ba ấn phải khác nhau (A‑D‑H). Ghép như A‑A‑D là không hợp lệ.

## 4. Hướng dẫn chơi

### Chuẩn bị

- Chơi đứng, dọn trống khoảng **2 m × 2 m**. Mặt đấu trường chính là **sàn phòng thật**, nên bạn phải cúi xuống được để cắm kiếm.
- Muốn chơi bằng tay không: vào **Settings → Movement tracking → Hand and body tracking** trong kính → bật, và bật **Auto switch between hands and controllers**.

### Điều khiển bằng tay (hand tracking)

| Việc | Thao tác |
|---|---|
| Mở/đóng túi đồ | Lật mu bàn tay trái lên (đồng hồ xanh ở cổ tay), **chạm đầu ngón trỏ phải vào mặt đồng hồ** |
| Bấm EQUIP / STORE | Chĩa tay phải vào nút (có tia sáng), **chụm ngón cái + ngón trỏ (pinch)** |
| Cầm kiếm | Đưa tay tới chuôi, **nắm cả bàn tay** hoặc **pinch** |
| Cắm kiếm | Đang nắm kiếm, **đâm nhanh xuống sàn** tới khi mũi chạm sàn |
| **A – Kiếm Chỉ** | Duỗi **ngón trỏ + ngón giữa**, gập các ngón còn lại, **chỉ vào quái** |
| **D – Thuẫn Chưởng** | **Xòe bàn tay**, lòng bàn tay hướng ra trước mặt |
| **H – Tâm Ấn** | **Áp lòng bàn tay lên ngực**, lòng bàn tay hướng vào người |
| **Ultimate** | **Hai tay cùng làm Kiếm Chỉ**, giữ 0,5 giây |

Khi đang cầm kiếm, nhận diện ấn tạm tắt để tư thế cầm không bị hiểu nhầm thành ấn. Thả kiếm ra rồi mới kết ấn.

### Điều khiển bằng tay cầm Touch

| Việc | Nút |
|---|---|
| Mở/đóng túi đồ | **Nhấn cần analog trái**, hoặc chạm tay cầm phải vào đồng hồ ở cổ tay trái |
| Bấm nút trong túi đồ | Chĩa tia tay cầm phải vào nút, bóp **cò (trigger)** |
| Cầm kiếm | Giữ **grip** (nút bên hông) ở chuôi kiếm |
| Ấn A / D / H | **A** / **B** / **X** |
| Ultimate | **Y** |

### Mẹo

- Cắm kiếm ở chỗ trống giữa bạn và quái. Nếu cắm quá sát quái hoặc ra ngoài đấu trường, game sẽ từ chối và kiếm bay về chỗ cũ.
- Mở đầu bằng **AA** để gây sát thương, dùng **DD** hoặc **DH** khi quái sắp tung chiêu mạnh, và **HH** để vừa hồi máu vừa hoàn AP.
- Dành AP cho **ADH** (9 AP), vì đây là kỹ năng mạnh nhất của từng pet.

## 5. Cài đặt & build lên kính

**Yêu cầu:** Unity **6000.0.78f1** (kèm module Android Build Support), Git LFS, và Meta Quest đã bật **Developer Mode**.

```bash
git lfs install
git clone https://github.com/TOP1FreeFileVn/XR124.git
```

1. Mở project bằng Unity Hub. Project đã được chuyển sẵn sang nền tảng **Android**. Scene chính là `Assets/Scenes/SampleScene.unity`.
2. Cắm Quest bằng cáp USB‑C. Lần đầu cắm, đeo kính rồi bấm **Allow USB debugging** và tick **Always allow**.
   Kiểm tra bằng `adb devices`: Quest phải hiện là `device`.
3. Vào **File → Build Profiles → Android** → ở **Run Device** chọn đúng Quest → **Build And Run**.

Cấu hình đã sẵn trong project:
- OpenXR có Meta XR feature và Oculus Touch profile.
- Render single-pass instanced, Vulkan, ARM64 + IL2CPP.
- Tracking origin theo mặt sàn (Floor Level).
- Hand tracking ở chế độ Controllers + Hands.

## 6. Cấu trúc project

```
Assets/
├─ Scripts/Combat/
│  ├─ Data/        Enum, bảng khắc hệ, bảng chiêu (CombatRules), PetDefinition, thư viện icon hệ
│  ├─ Runtime/     PetCombatant, CombatMatch, DamageCalculator, PetAutoBattler (di chuyển + đánh thường + animation)
│  ├─ Abilities/   Kỹ năng riêng Zeru / Onea / Tiwo
│  ├─ Input/       HandSkeletonReader, HandSealRecognizer (nhận diện ấn), SealComboCaster (ghép combo + nút tay cầm)
│  ├─ Summon/      SummonSword (cắm kiếm), SummonPortal (pháp trận), BattleSummoner (luồng triệu hồi)
│  ├─ Placement/   ArenaPlacement (kiểm tra vị trí hợp lệ trên sàn đấu trường)
│  ├─ UI/          PetHealthBar (HUD), WristInventory (túi đồ đồng hồ)
│  ├─ Debug/       BattleDebugLabel, PetCombatDummyAI, GameplayVideoRecorder (quay video giới thiệu)
│  └─ Editor/      Menu dựng scene/art, HUD builder, postprocessor animation Zeru
├─ Art/
│  ├─ Pets/        Zeru, DogSmall (Onea), Dragon (Tiwo): model, texture, Animator Controller
│  ├─ Arena/       Model đấu trường (dựng trong Blender)
│  ├─ Weapons/     Katana triệu hồi
│  ├─ VFX/         MagicCircle.shader + material
│  ├─ Sky/         Skybox tự sinh
│  └─ UI/          Icon 8 hệ, vật liệu đồng hồ
├─ Combat/Data/    Asset dữ liệu: CombatRules, ElementChart, Pet_Zeru / Pet_Onea / Pet_Tiwo
└─ Scenes/SampleScene.unity
Docs/              GDD (PDF/HTML), hướng dẫn chơi
```

## 7. Công cụ trong Editor

Menu **XR124/Combat**:

| Menu | Chức năng |
|---|---|
| Create Default Data | Tạo asset dữ liệu mặc định (luật, bảng hệ, 3 pet) |
| Convert Scene To VR Arena | Dựng đấu trường VR cơ bản |
| Create Summon Battle Setup | Dựng toàn bộ luồng trận: tay, kiếm, pháp trận, pet, trận đấu |
| Use Hands Instead Of Controllers | Ẩn model tay cầm, luôn hiện bàn tay để kết ấn |
| Use Arena Model | Thay đấu trường tạm bằng model Arena.fbx |
| Upgrade Summon Portal (Magic Circle) | Thay pháp trận cũ bằng shader MagicCircle, bỏ cổng của quái |
| Setup Zeru & Katana Art | Import model/animation pet, katana, skybox, icon hệ, HUD |
| Create Wrist Inventory | Dựng túi đồ đồng hồ trên cổ tay trái |

**Quay video gameplay (chỉ để giới thiệu):** vào Play mode, thêm component `GameplayVideoRecorder` vào một GameObject bất kỳ. Frame và phụ đề được lưu ở `Recordings/`; thư mục này không đưa lên git. Ghép thành video bằng ffmpeg.

## 8. Lỗi thường gặp

| Hiện tượng | Cách xử lý |
|---|---|
| Unity không thấy kính / `unauthorized` | Đeo kính bấm **Allow USB debugging**; rút cáp ra cắm lại nếu không thấy hộp thoại |
| Build chạy nhầm sang BlueStacks/emulator | Tắt emulator hoặc chọn đúng Quest ở **Run Device** |
| Không thấy tay | Bật hand tracking trong Settings của kính; bỏ tay cầm xuống vài giây |
| Cắm kiếm không ăn | Đâm nhanh hơn và cho mũi kiếm chạm hẳn sàn; không cắm sát quái |
| Kết ấn không nhận | Thả kiếm trước; giữ tay trong tầm nhìn của kính; với Kiếm Chỉ phải chỉ vào quái |
| Clone về thiếu model/texture | Chạy `git lfs install` rồi `git lfs pull` |
