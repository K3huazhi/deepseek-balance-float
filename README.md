# DeepSeek 余额悬浮窗

一个用于实时显示 DeepSeek API 账户余额的 Windows 桌面悬浮窗，采用 WinUI 3 / Fluent Design 风格，支持系统云母（Mica）背板、亮暗主题切换、多 Key 管理与用量统计。

> 纯 Python + PySide6 实现，无需安装 .NET 或 Windows App SDK，任何 Windows 11 机器（x64）都能直接运行。

---

## 功能特性

### 界面
- **系统云母材质**：调用 DWM 的 Mica 背板（Windows 11），窗口透明无边框、圆角、隐藏系统描边
- **亮色 / 暗色 / 跟随系统**：一键切换，切换时带回波扩散的换肤动画
- **余额动效**：数字滚动计数，余额减少时抖动并飘出红色 `-¥0.01`
- **拖动定位**：拖动窗口任意位置移动，位置自动记忆；可置顶；可开机自启

### 数据
- **余额展示**：实时余额、可用状态、充值余额、赠送余额、今日消费
- **近 7 日消耗柱状图** + 按近期均耗估算的可用天数
- **多 Key 支持**：最多保存 10 个 API Key，最多同时显示 3 个
  - 显示 ≥ 2 个时自动切换为紧凑视图，只显示各 Key 的余额与今日消耗
- **Key 命名**：可为每个 Key 命名，不命名时默认取 `sk-` 之后的六位

### 安全
- API Key 保存在 **Windows 凭据管理器**（DPAPI 加密、按用户隔离），不写入任何文件
- 界面中已保存的 Key **隐藏中间六位**（如 `sk-a81117a2c5d******c97b01ceb0921c8`）
- 界面设置与用量统计存放在注册表 `HKCU\Software\DeepSeekBalance`，**不生成配置文件**
- 连点两下余额可立即刷新；右键菜单可管理 Key、切换主题、置顶、开机自启

---

## 截图

| 亮色主题 | 暗色主题 |
| --- | --- |
| ![light](docs/preview-light.png) | ![dark](docs/preview-dark.png) |

| 展开详情 | 多 Key 视图 | API Key 管理 |
| --- | --- | --- |
| ![details](docs/preview-details.png) | ![multi](docs/preview-multi.png) | ![keys](docs/preview-keys.png) |

---

## 环境要求

- Windows 11 x64（Windows 10 也可运行，但没有云母材质）
- Python 3.9+（开发时使用 3.12）
- 依赖：`PySide6-Essentials`（含 Qt Quick / Qt Quick Shapes / QtSvg）

```bash
pip install -r requirements.txt
```

---

## 运行

```bash
python balance_float.py
```

或双击 `run.bat`（无控制台窗口）。

首次启动如果没有配置 Key，会自动弹出 **API Key 管理**窗口：

1. 复制你的 DeepSeek API Key（`sk-...`）
2. 在输入框中 **右键即可直接粘贴**
3. 回车或点「添加」保存

之后的 Key 管理可以从右键菜单进入。

---

## 使用说明

| 操作 | 说明 |
| --- | --- |
| 左键拖动 | 移动窗口，位置自动保存 |
| 双击余额 | 立即刷新 |
| 右键单击 | 打开菜单：刷新 / API Key 管理 / 主题 / 置顶 / 开机自启 / 退出 |
| 右上角 ⌄ / ⌃ | 展开或收起详情面板 |
| 右上角 ☀ / ☾ | 切换亮色 / 暗色 |
| 点 Key 名称 | 在底部改名条中重命名（Enter 保存，Esc 取消） |
| 拨动开关 | 控制该 Key 是否显示在悬浮窗（最多 3 个） |
| 垃圾桶图标 | 删除 Key（有二次确认） |

### 多 Key 显示规则

- 保存上限 **10 个**
- 同时显示上限 **3 个**，至少保留 **1 个**
- 显示数量 **≥ 2** 时，悬浮窗自动隐藏详情面板，只显示每个 Key 的「余额 + 今日消耗」

---

## 数据存储位置

| 数据 | 位置 |
| --- | --- |
| API Key（含名称、显示状态） | Windows 凭据管理器，目标名 `DeepSeekBalance:api_key` |
| 主题 / 位置 / 置顶 / 展开 / 刷新间隔 / 接口地址 / 用量统计 | 注册表 `HKCU\Software\DeepSeekBalance` |

程序运行期间 **不会在程序目录生成任何文件**。查看或清理凭据可在「控制面板 → 凭据管理器 → Windows 凭据」中操作。

默认请求地址为 `https://api.deepseek.com/user/balance`，如需修改可编辑注册表项 `url`。

---

## 打包为单文件 EXE

```bash
pip install pyinstaller
pyinstaller --noconfirm --clean --onefile --windowed ^
    --name "DeepSeek余额悬浮窗" ^
    --icon icon.ico ^
    balance_float.py
```

产物在 `dist\` 目录下，单文件约 58 MB，目标机器无需安装 Python。

> 单文件模式启动时会把运行时解压到 `%TEMP%\_MEIxxxx`，退出自动清理；这不涉及任何配置或用户数据。

---

## 项目结构

```
balance_float.py      # 全部源码（Python 后端 + 内嵌 QML 界面）
run.bat               # 无控制台启动脚本
requirements.txt      # 依赖
LICENSE               # MIT 许可
README.md
```

界面使用 Qt Quick（QML 以字符串内嵌，便于单文件打包），后端使用 PySide6 的 `QObject` 属性/槽与 QML 双向绑定。

---

## 注意事项

- 余额每 5 秒以上轮询一次（默认 3 秒，最小 5 秒），频繁刷新可能触发接口限流
- 统计的「今日消耗」由前后两次余额差推算，只在有消耗时增长
- 本程序会向 DeepSeek 官方接口发送请求，需要能访问 `api.deepseek.com`

---

## 许可证

[MIT](LICENSE)
