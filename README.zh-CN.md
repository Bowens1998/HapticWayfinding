# Haptic Wayfinding（体感服方位提示）复现

[English](README.md) | **简体中文**

复现 ICIC Lab 视频 [Haptic suit for wayfinding](https://youtu.be/SMfNwah_cNI)（Eric Jing Du，2019-11，51 秒）。
视频简介原文："Using a haptic suit to guide directions without seeing."

## 1. 要实现的功能

受试者戴着 VR 头显站在房间中间的玻璃隔间里，隔间外有一个"小人"在走动。
体感服上**对应方位的马达会振动**，用来告诉受试者小人在自己的哪个方位，他不需要看见小人。

计算方法：
1. 取人的**正前方向量**（默认取头显朝向，投影到地面上）；
2. 取人指向小人的向量（小人当作一个点，同样投影到地面）；
3. 求两者的**有符号夹角** = bearing（0° 正前，+90° 右，−90° 左，±180° 正后）；
4. 把 bearing 分到 8 个方向（每个 45°），每个方向驱动**相邻的两列**马达（每列 5 个马达一起振）。下表是 2026-11-18 在实验室实测后确定的：

| 方向 | 振动的两列（穿戴者视角，从左到右数） |
|---|---|
| 正前 (0°) | 前片第 2、3 列（中间两列）|
| 右前 (+45°) | 前片第 3、4 列 |
| 正右 (+90°) | 前片最右列 + 后片最右列 |
| 右后 (+135°) | 后片第 4、3 列 |
| 正后 (180°) | 后片第 2、3 列（中间两列）|
| 左后 (−135°) | 后片第 2、1 列 |
| 正左 (−90°) | 前片最左列 + 后片最左列 |
| 左前 (−45°) | 前片第 1、2 列 |

按 `M` 还可以切换成单列（SingleColumn）或相邻两列插值（Interpolated）模式。

## 2. 硬件

| 项目 | 型号 / 说明 |
|---|---|
| 头显 | HTC VIVE Pro（Lighthouse 基站 + SteamVR）。**Link Box 只有 Mini DisplayPort 输入**，需要 DP / Mini DP 线，HDMI 不行 |
| 体感服 | **bHaptics Tactot**（已在实验室确认；前 20 + 后 20 个马达），通过蓝牙连到 bHaptics Player 3.6.13 |
| 软件 | Unity **2022.3.62f3 LTS** + URP（通用渲染管线）+ OpenXR（SteamVR runtime）|
| 振动驱动 | 通过 bHaptics Player 的本地 WebSocket（`ws://127.0.0.1:15881/v2/feedbacks`）直接发送每个马达的强度，不需要 bHaptics SDK 或 App ID |

## 3. 场景（写实版）

一个 14 × 14 m、层高 3.2 m 的实验室大厅，所有贴图和模型都由编辑器脚本程序化生成，**没有使用任何外部素材**：

- 抛光混凝土地面、乳胶漆墙面、矿棉吸音板吊顶 + 16 块 LED 灯盘、踢脚线、两根结构柱
- 东墙三扇铝框窗（窗外有天空、地面、远处楼房），窗下有暖气片；太阳光从窗户照进来，在地面投下光斑
- 南墙木门 + 门框 + 把手 + 安全出口灯；消防栓；墙角的 SteamVR 基站
- 西墙三个工位（木桌、显示器、键盘、办公椅）；北墙书架、白板、挂钟
- 中间是 3 × 3 m 的铝框**玻璃隔间**，地上铺着 VR 地垫，受试者站在这里（对应视频里的小房间）
- **小人**：真人比例（约 1.77 m）的着装人台，有程序化的走路动画（摆腿、屈膝、摆臂、身体起伏）。脚步跟实际移动距离同步，不会滑步；停下时会慢慢转身面向受试者

渲染方面：URP 管线，HDR + ACES 色调映射、轻微泛光和暗角、SSAO（环境光遮蔽）、4×MSAA、柔和阴影。灯盘的光和多次反弹光**提前烘焙**（Progressive GPU lightmapper），太阳实时投影；移动的小人通过 Light Probe 接收烘焙光，房间里有一个盒体投影的反射探针。

## 4. 目录结构

```
HapticWayfinding/
├── README.md / README.zh-CN.md      # 本说明（英文 / 中文）
├── CLAUDE.md                        # 给 Claude Code 的项目说明（在本目录打开 Claude 时会自动读取）
├── tools/
│   ├── vest_test.py                 # 不需要 Unity，单独测试背心（马达顺序 / 列方向）
│   └── latency_test.ps1             # 不需要 Unity，测振动延迟会不会越积越大
└── HapticWayfindingVR/              # Unity 工程
    └── Assets/HapticWayfinding/
        ├── Scripts/Haptics/         # 背心布局与映射、bHaptics 通信（差量发送）、校准
        ├── Scripts/Guidance/
        │   ├── DirectionalHapticGuide.cs    # ★核心：前向量 vs 小人 → 夹角 → 相邻两列马达
        │   ├── TargetFigureController.cs    # 小人的移动：鼠标拖动 / 方向键 / 环绕 / 随机跳 / 数字键定位
        │   └── ProceduralWalker.cs          # 人台的走路动画
        ├── Scripts/Rig/             # 头显位姿、无头显时的桌面模拟、视觉条件、实验者俯视图
        ├── Scripts/UI/              # 实验者 HUD、背心实时可视化、方向连线
        ├── Scripts/Logging/         # CSV 记录（位姿、夹角、40 个马达强度）
        ├── Editor/
        │   ├── ProjectSetup.cs      # 一键配置：图层 / Input / OpenXR / 打包
        │   ├── RenderSetup.cs       # URP、SSAO、后处理
        │   ├── AssetFactory.cs      # 程序化贴图、材质、网格
        │   ├── SceneBuilder.cs      # 生成写实场景、人台，并烘焙光照
        │   └── PreviewCapture.cs    # 渲染预览截图
        ├── Scenes/                  # 生成的场景 + 烘焙光照数据
        ├── Generated/               # 生成的贴图 / 材质 / 网格
        └── Rendering/               # URP 管线资源、后处理配置、光照设置
```

## 5. 部署到实验室台式机

### 5.0 两条路线都要先装的东西

| 软件 | 说明 |
|---|---|
| **Steam + SteamVR** | 装好后打开 SteamVR，确认头显和两个基站图标都是绿色。然后 SteamVR → 设置 → OpenXR → 点 **"设为当前 OpenXR 运行时"** |
| **bHaptics Player** | 官网下载安装。打开背心电源 → 在 Player 里配对，直到 Tactot 显示已连接 |
| VIVE Pro 接线 | Link Box：**Mini DisplayPort**（接显卡的 DP 口，不能用 HDMI）+ USB 3.0 + 电源 |

> 蓝牙建议：台式机最好用一个独立的 USB 蓝牙适配器专门连背心，并在设备管理器里关闭蓝牙网卡的"允许计算机关闭此设备以节约电源"（见第 8 节）。

### 路线 A：直接运行打包好的程序（不需要装 Unity）

1. 在本仓库的 GitHub 页面 → 右侧 **Releases** → 下载最新的 `HapticWayfinding-win64.zip`。
2. 解压到任意文件夹（路径里最好不要有中文），双击 `HapticWayfinding.exe`。
3. 程序会自动连接 SteamVR 头显和 bHaptics Player，HUD 左上角应该显示 `HMD tracking` 和 `Vest: Connected`。
4. 先按 `C` 做一次列测试，确认背心左右方向正确（不对就按 `F6` / `F7`，会自动保存）。

### 路线 B：用 Unity 打开工程（需要修改或重新打包时）

1. 安装 **Unity Hub** 并登录（个人免费许可证即可）→ Installs → 安装 **Unity 2022.3.62f3**，勾选 *Windows Build Support (IL2CPP 可不选)*。版本一定要一致，否则会提示升级工程。
2. 克隆仓库：
   ```bash
   git clone https://github.com/Bowens1998/HapticWayfinding.git
   ```
3. Unity Hub → **Add** → **Add project from disk** → 选择仓库里的 `HapticWayfindingVR` 文件夹 → 打开。第一次打开要导入包、编译着色器，大约 5–10 分钟。
   - 如果弹出 *"enable the new Input System backends?"*，点 **No**（工程已经设为新旧两套输入都启用）。
4. 打开 `Assets/HapticWayfinding/Scenes/HapticWayfinding.unity`，点 **Play**。场景、烘焙光照、URP 和 OpenXR 设置都已经在仓库里，**不需要重新生成或烘焙**。
5. 打包 exe：菜单 **Haptic Wayfinding → 3. Rebuild Scene + Windows Build**（会重新生成场景并烘焙，需要几分钟），输出到 `HapticWayfindingVR/Builds/HapticWayfinding.exe`。只想打包、不想重新烘焙时，用 Unity 自带的 File → Build Settings → Build。
6. 其他菜单（一般用不到）：**1. Configure Project**（图层 / 输入 / OpenXR）、**Configure URP Rendering**、**2. Build Demo Scene**（重新生成场景并烘焙）、**2b**（不烘焙，快速生成）、**Capture Preview Stills**（渲染预览截图到 `HapticWayfindingVR/Previews/`）。

### 常见问题

| 现象 | 处理 |
|---|---|
| HUD 显示 `No HMD - desktop simulator` | SteamVR 没开，或者 OpenXR 运行时不是 SteamVR（看 5.0 第一行）。先打开 SteamVR 再启动程序 |
| HUD 显示 `bHaptics Player not reachable` | bHaptics Player 没开。打开后程序会自动重连 |
| HUD 显示已连接但背心不振 | Player 里背心是否显示已连接；按 `F5` 切换发送格式再试 |
| 振动方向左右反了 | 按 `F6`（前片）/ `F7`（后片），自动保存 |
| 帧率低、头显里画面卡 | 显卡较弱时，在 Unity 里打开 `Assets/HapticWayfinding/Rendering/HW_URP.asset`，把 MSAA 改成 2x 或关闭，再关掉 `HW_URP_Renderer` 里的 SSAO |

没有头显时程序会自动进入桌面模拟模式：W/A/S/D 移动，Q/E 转身，按住右键拖动鼠标看四周。

## 6. 实验室调试清单（按顺序）

1. **背心**：开机 → 打开 bHaptics Player → 确认 Tactot 显示为已连接。
2. **单独测背心**（不需要 Unity）：
   ```
   pip install websocket-client
   python tools/vest_test.py columns   # 8 列依次振，请穿戴者说出振动位置
   ```
   左右颠倒时，在 demo 里按 `F6`（前片）/ `F7`（后片）翻转，设置会自动保存。
3. **头显**：SteamVR 打开且基站正常，头显和基站图标都是绿色。
4. **Unity**：Play → HUD 显示 `HMD tracking` 和 `Vest: Connected` → 按 `C` 做列测试，按 `K` 做扫描。
5. **功能验证**：按数字键 1–8 把小人放到各个方位，穿戴者应该在对应位置感到振动；转身时振动的列应该跟着转。

## 7. 实验者按键

| 键 | 功能 |
|---|---|
| 1–8 | 把小人放到相对于受试者当前朝向的 0/45/…/315° 方向（半径 2.5 m）|
| 方向键 / 地图上左键拖动 | 手动移动小人（小人不会进入玻璃隔间，也不会走进墙边的家具）|
| G | 小人运动方式：Manual / Orbit（环绕）/ RandomJump（随机方位跳）|
| M | 编码：AdjacentPair（相邻两列，默认）/ SingleColumn（单列）/ Interpolated（插值）/ Off |
| N | 距离编码：None / PulseRate（越近脉冲越快）/ Intensity（越近越强）|
| V | 头显画面：FigureVisible / FigureHidden（默认，看不见小人）/ Blindfold（全黑）|
| Tab | 桌面窗口布局：头显画面 + 俯视地图 / 只显示地图 / 只显示头显画面 |
| H | 振动总开关；`+` / `-` 调总强度 |
| C / K / J / X | 列测试 / 旋转扫描 / 逐马达测试 / 停止测试 |
| L | 开始 / 停止记录 CSV（保存在 `HapticWayfindingVR/Logs/`，不会上传到 git）|
| F5 / F6 / F7 / F8 | 发送格式（VestFront+VestBack ↔ Vest 40 点）/ 前片左右翻转 / 后片左右翻转 / 恢复默认（自动保存）|
| F1 / F2 | 隐藏或显示帮助 / 背心可视化 |

## 8. 已知问题与注意事项

- **振动延迟越积越大、背心用一阵子就断开**：实测 bHaptics Player 本身响应很快（替换帧和 turnOff 都在 1 ms 级），问题出在 Player 到背心的**蓝牙链路**。程序已经改成只发变化的部分（静止时每秒 1 条，拖动时最多约 16 条/秒），HUD 上会显示 msg/s。如果还有问题：
  - 设备管理器 → 蓝牙 → 蓝牙网卡 → 电源管理 → 取消勾选"允许计算机关闭此设备以节约电源"；
  - 断开同一块蓝牙网卡上的其他设备（蓝牙鼠标、键盘、手机）；
  - 最稳的办法：给背心单独插一个 USB 蓝牙适配器；
  - 用 `tools/latency_test.ps1` 可以区分是"变化太频繁"还是"蓝牙链路本身在退化"。
- 视频左下角有一排红 / 黄圆点，含义看不出来，本项目没有复现。
- 方向默认取**头显**朝向。如果需要"躯干朝向"（只转头时振动不变），可以在胸前绑一个 VIVE Tracker，再把 `DirectionalHapticGuide.body` 指向它。
- **安全**：Blindfold 模式下受试者完全看不见，旁边一定要有人看护，房间里的障碍物要清空。
