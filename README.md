# DeepSeek 余额悬浮窗

一个用于实时显示 DeepSeek API 账户余额的 Windows 桌面悬浮窗，采用 WinUI 3 / Fluent Design 风格，支持系统云母（Mica）背板、亮暗主题、多 Key 管理与用量统计。

> 原生 C# + WPF 实现，单个约 116 KB 的 exe，无任何外部依赖；Windows 10 1903+ / 11 双击即用。

---

## 下载

[![Release](https://img.shields.io/github/v/release/K3huazhi/deepseek-balance-float?label=release)](https://github.com/K3huazhi/deepseek-balance-float/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/K3huazhi/deepseek-balance-float/total)](https://github.com/K3huazhi/deepseek-balance-float/releases)

**一个版本，双击即用。**

| 版本 | 体积 | 依赖 | 说明 |
| --- | --- | --- | --- |
| **v2.1 原生版** | **116 KB** | 无（.NET Framework 4.8 系统自带） | 推荐 |
| v1.x Python 版 | 55.8 MB / 126 KB | 内置运行时 / 需 Python | 已归档，不再更新 |

### v2.1 原生版（推荐）

**[⬇ DeepSeek-Balance-Float-v2.1.0.exe](https://github.com/K3huazhi/deepseek-balance-float/releases/download/v2.1.0/DeepSeek-Balance-Float-v2.1.0.exe)** · 116 KB

SHA256：`5c6af7489d80054073666ec1848432a24e3c3cdc0dfdecef0a51b42542125817`

Windows 10 1903+ 与 Windows 11 自带 .NET Framework 4.8，所以**不需要安装 Python，也不需要装任何运行时**，
双击即用，启动不到 1 秒。

> 使用自签名证书（`CN=K3huazhi`）签名。该证书不受公共 CA 信任，未导入证书的机器上
> SmartScreen 仍可能提示「未知发布者」，点「更多信息 → 仍要运行」即可。

升级说明：Key 存在 Windows 凭据管理器、设置与统计存在注册表，
与 Python 版使用相同位置，**从旧版本升级不会丢失 Key、命名和历史消耗记录**。

历史版本见 [Releases](https://github.com/K3huazhi/deepseek-balance-float/releases)。

---

## 功能特性

### 界面
- **系统云母材质**：调用 DWM 的 Mica 背板（Windows 11），窗口透明无边框、圆角、隐藏系统描边
- **三个页面，一个窗口**：主界面 / 设置 / API Key 管理 都在同一窗口内切换，不弹新窗口
- **水波纹过渡**：点左上角鲸鱼进入设置时，三圈波纹从鲸鱼位置扩散，
  波纹中点切换页面；离开设置同样从鲸鱼位置扩散
- **Key 页横向滑动**：设置 → API Key 管理 从右侧滑入，返回时滑回
- **主题按钮三态**：右上角图标显示当前模式（☀ 亮色 / ☾ 暗色 / Auto 自动），点一下循环切换；
  换肤时颜色从点击的位置扩散
- **自动模式按日出日落**：白天亮色、夜间暗色。日出日落按当天日期与经纬度计算
  （默认北纬 35°、经度由时区推算，可用注册表 `lat` / `lon` 覆盖）；
  在自动模式下手动切换一次即固定为手动模式
- **余额动效**：数字滚动计数，余额减少时抖动并飘出红色 `-¥0.01`
- **拖动定位**：5 px 起拖（手抖不会误拖），靠近屏幕边缘 12 px 自动吸附，
  位置自动记忆，多显示器各自吸附
- **失焦自动返回**：停留在设置页或 API Key 页时，窗口失去焦点满 10 秒会自动回到主界面
- **可置顶、可开机自启**

### 数据
- **余额展示**：实时余额、可用状态、充值余额、赠送余额、今日消费
- **每 5 秒自动刷新**，右键可立即刷新
- **近 7 日消耗柱状图** + 按近期均耗估算的可用天数
- **多 Key 支持**：最多保存 10 个 API Key，最多同时显示 3 个
  - 显示 ≥ 2 个时自动切换为紧凑视图，只显示各 Key 的余额与今日消耗
- **Key 命名**：可为每个 Key 命名，不命名时默认取 `sk-` 之后的六位

### 安全
- API Key 保存在 **Windows 凭据管理器**（DPAPI 加密、按用户隔离），不写入任何文件
- 界面中已保存的 Key **隐藏中间六位**（如 `sk-a81117a2c5d******c97b01ceb0921c8`）
- 界面设置与用量统计存放在注册表 `HKCU\Software\DeepSeekBalance`，**不生成配置文件**

---

## 截图

| 亮色主题 | 暗色主题 |
| --- | --- |
| ![light](docs/preview-light.png) | ![dark](docs/preview-dark.png) |

| 设置页 | API Key 管理 |
| --- | --- |
| ![settings](docs/preview-settings.png) | ![keys](docs/preview-keys.png) |

| 展开详情 | 多 Key 视图 |
| --- | --- |
| ![details](docs/preview-details.png) | ![multi](docs/preview-multi.png) |

---

## 环境要求

- Windows 10 1903+ / Windows 11（x64）
- 无需安装任何运行时（.NET Framework 4.8 为系统组件）

---

## 运行

双击 `DeepSeek-Balance-Float.exe` 即可。首次启动没有 Key 时会直接进入 **API Key 管理**页：

1. 复制你的 DeepSeek API Key（`sk-...`）
2. 在输入框中 **右键即可直接粘贴**
3. 回车或点「添加」保存

之后再管理 Key：点左上角鲸鱼 → 设置 → API Key 管理。

> 建议把 exe 放在普通目录（桌面、D 盘等）。某些沙箱/同步盘目录会带低完整性标记，
> 这会让程序无法写入注册表。

---

## 使用说明

| 操作 | 说明 |
| --- | --- |
| 左键拖动 | 移动窗口（5 px 起拖），靠近屏幕边缘自动吸附，位置自动保存 |
| 右键单击 | 立即刷新余额 |
| 点左上角鲸鱼 | 进入设置页（水波纹过渡） |
| 右上角 ⌄ / ⌃ | 展开或收起详情面板 |
| 右上角 ☀ / ☾ / Auto | 循环切换：亮色 → 暗色 → 自动 |
| 点 Key 名称 | 在底部改名条中重命名（Enter 保存，Esc 取消） |
| 拨动开关 | 控制该 Key 是否显示在悬浮窗（最多 3 个） |
| 复制图标 | 把完整 Key 复制到剪贴板（界面上只显示掩码） |
| 垃圾桶图标 | 删除 Key（有二次确认） |

### 设置页

开机自启、窗口置顶、显示模式（亮色 / 暗色 / 自动）、API Key 管理、退出程序。

### 多 Key 显示规则

- 保存上限 **10 个**
- 同时显示上限 **3 个**，至少保留 **1 个**
- 显示数量 **≥ 2** 时，悬浮窗自动隐藏详情面板，只显示每个 Key 的「余额 + 今日消耗」

---

## 数据存储位置

| 数据 | 位置 |
| --- | --- |
| API Key（含名称、显示状态） | Windows 凭据管理器，目标名 `DeepSeekBalance:api_key` |
| 主题 / 位置 / 置顶 / 展开 / 开机自启 / 接口地址 / 用量统计 | 注册表 `HKCU\Software\DeepSeekBalance` |

程序运行期间 **不会在程序目录生成任何文件**。查看或清理凭据可在「控制面板 → 凭据管理器 → Windows 凭据」中操作。

默认请求地址为 `https://api.deepseek.com/user/balance`，如需修改可编辑注册表项 `url`。

---

## 从源码构建

原生版用系统自带的 `csc.exe` 编译，不需要安装 SDK、Visual Studio 或任何 NuGet 包：

```powershell
$bin = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $bin 'WPF'
$refs = @(
  (Join-Path $wpf 'PresentationFramework.dll'),
  (Join-Path $wpf 'PresentationCore.dll'),
  (Join-Path $wpf 'WindowsBase.dll'),
  (Join-Path $bin 'System.Xaml.dll'),
  (Join-Path $bin 'System.Web.Extensions.dll'),
  (Join-Path $bin 'System.Core.dll')
) | ForEach-Object { "/r:$_" }

& "$bin\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 /platform:anycpu `
  /win32manifest:app.manifest /win32icon:icon.ico $refs `
  /out:DeepSeek-Balance-Float.exe core.cs program.cs ui_main.cs ui_keypage.cs AssemblyInfo.cs
```

产物就是单个约 115 KB 的 exe。

---

## 项目结构

```
native/
  core.cs          Win32 / DWM 云母 / 凭据管理器 / 注册表 / HTTP / 统计 / 日出日落
  ui_main.cs       主界面 + 设置页 + 视图切换（水波纹、横向滑动）
  ui_keypage.cs    内嵌 API Key 管理页
  program.cs       入口、单实例互斥、鲸鱼 SVG 路径、诊断开关
  AssemblyInfo.cs  版本信息
  app.manifest     PerMonitorV2 高 DPI + asInvoker

balance_float.py   v1.x 的 Python 实现（已归档，保留作参考）
launcher.cs        v1.x 精简版启动器（已归档）
```

### 技术选择

- **云母**：`DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE, 2)` 配合
  `DwmExtendFrameIntoClientArea` 与透明合成目标；圆角用 `DWMWA_WINDOW_CORNER_PREFERENCE`
- **换肤波纹**：椭圆 `ScaleTransform` 动画，圆心取点击位置
- **进出设置**：换肤是纯色变化，而同色页面切换用纯色波看不见，所以改用**带描边的同心圆环**
  （文字色、透明度 0.6→0）做水波纹，中点切换页面
- **Key 存储**：`CredReadW / CredWriteW`，与 Python 版同一目标名 `DeepSeekBalance:api_key`
- **开机自启**：写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，
  不用 Startup 目录的 bat（实测会被安全软件拦截）
- **拖动**：自行处理鼠标捕获 + `GetCursorPos`（`DragMove` 是阻塞调用，拿不到实时坐标就无法做吸附）

---

## 注意事项

- 余额每 **5 秒** 自动刷新一次，同时显示多个 Key 时是每个 Key 各请求一次
- 统计的「今日消耗」由前后两次余额差推算，只在有消耗时增长
- 本程序会向 DeepSeek 官方接口发送请求，需要能访问 `api.deepseek.com`

---

## 许可证

[MIT](LICENSE)
