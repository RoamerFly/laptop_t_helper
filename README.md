<p align="center">
  <img src="./docs/icon.png" width="144" alt="笔记本温控助手图标" />
</p>

<h1 align="center">笔记本温控助手 (Laptop Thermal Helper)</h1>

<p align="center">面向 Windows、Linux 与 macOS 的本地跨平台 CPU、GPU 与 SSD 温度监控与安全降温工具：用中文状态、实时图表、桌面悬浮窗与终端仪表盘，让硬件状态更容易看懂。</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue" alt="Platform" />
  <img src="https://img.shields.io/badge/Tests-116%20Passed-brightgreen" alt="Tests" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License" />
</p>

<p align="center"><a href="#笔记本温控助手">简体中文</a> · English（计划中）</p>

<p align="center"><a href="#功能特性">功能特性</a> · <a href="#平台支持">平台支持</a> · <a href="#快速开始">快速开始</a> · <a href="#cli-与守护进程">CLI 与守护进程</a> · <a href="#开发者指南">开发者指南</a> · <a href="#致谢">致谢</a> · <a href="#许可证">许可证</a></p>

---

## 功能特性

- **多平台只读硬件监控**：
  - **Windows**：通过 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 底层内核驱动只读采集 CPU、GPU、SSD 传感器；
  - **Linux**：原生对接 Linux 内核 `/sys/class/hwmon` 与 `/proc/stat`，零外部驱动依赖，支持 Intel Core、AMD Ryzen、AMD/NVIDIA 显卡及 NVMe 存储；
  - **macOS**：通过 `sysctl`、SMC 与供电状态接口采集硬件环境与指标。
- **真实硬件物理降温（零危险写入 EC）**：
  - **Windows**：调用系统原生电源管理 API（`PowrProf.dll` / `powercfg`），在持续高温时动态限制处理器最大状态（90%~95%），截断激进睿频功耗（TDP 从 60~90W 骤降至 15~28W），物理降温 10°C~25°C；温度稳定后迟滞自动恢复；
  - **Linux**：对接标准 Sysfs 节点（Intel `intel_pstate/no_turbo`、AMD `cpufreq/boost` 或 `scaling_max_freq`），以同样的安全快照与回滚机制实现一键防过热降温。
- **双端体验**：
  - **Windows 桌面端 (WPF)**：响应式卡片、实时趋势图、深浅主题无缝切换、系统托盘常驻与**桌面温度半透明置顶悬浮窗**；
  - **跨平台终端端 (CLI & Daemon)**：彩色 ANSI 实时仪表盘（TUI）、无头后台守护模式（`--daemon`，支持 Linux `systemd`）、结构化数据输出（`--json` / `--status`，无缝集成 Waybar、Polybar、i3blocks 与 tmux）。
- **安全第一与本地离线**：
  - 零网络外联、无遥测上报，数据与 CSV 日志仅保留在本地；
  - 绝不写入私有 EC 寄存器或 BIOS，确保不造成硬件死锁与死机风险。

---

## 平台支持

| 平台 | 呈现形式 | 硬件采集实现 | 自动降温实现 | 状态 |
|---|---|---|---|---|
| **Windows 11 / 10** | 桌面 GUI (WPF) + 托盘 + 悬浮窗 + CLI | LibreHardwareMonitor 内核驱动 | `PowrProf` (最大处理器状态限制) | ✅ 完全支持 |
| **Linux (Ubuntu/Debian/Arch/Fedora/RHEL)** | 终端仪表盘 (TUI) + 后台守护 (Daemon) + CLI | Linux 内核 Sysfs (`/sys/class/hwmon`) | `intel_pstate/no_turbo` 或 `cpufreq/boost` | ✅ 完全支持 |
| **macOS (Sonoma / Sequoia / Apple Silicon / Intel)** | 终端仪表盘 (TUI) + CLI | `sysctl` + SMC 状态探测 | 安全只读监测守护 | ✅ 完全支持 |

---

## 快速开始

### 1. Windows 桌面版构建与运行

需要 **.NET 10 SDK** 与 Git。

```powershell
# 克隆仓库
git clone --recurse-submodules https://github.com/RoamerFly/laptop_t_helper.git
cd laptop_t_helper

# 一键还原、构建 Release、运行 116 项测试并打包发布
.\build.bat
```

构建成功后，在 `dist_windows` 目录下生成自包含的独立可执行文件：

```powershell
# 运行真实硬件监控与托盘/悬浮窗模式
.\dist_windows\LaptopThermalHelper.App.exe

# 运行演示模拟模式
.\dist_windows\LaptopThermalHelper.App.exe --mock

# 静默后台启动（开机自启模式）
.\dist_windows\LaptopThermalHelper.App.exe --background
```

### 2. Linux 与 macOS 构建与运行

在 Linux 或 macOS 终端中运行：

```bash
# 克隆仓库
git clone --recurse-submodules https://github.com/RoamerFly/laptop_t_helper.git
cd laptop_t_helper

# 赋予执行权限并构建
chmod +x ./build.sh
./build.sh
```

构建完成后，程序将发布至 `dist_linux/`（Linux）或 `dist_macos/`（macOS）：

```bash
# 启动交互式彩色终端仪表盘
./dist_linux/LaptopThermalHelper.Cli

# 单次输出格式化 JSON（适合嵌入脚本或状态栏）
./dist_linux/LaptopThermalHelper.Cli --json

# 单次输出简要状态
./dist_linux/LaptopThermalHelper.Cli --status

# 后台守护模式运行
./dist_linux/LaptopThermalHelper.Cli --daemon
```

---

## CLI 与守护进程

跨平台命令行程序 `LaptopThermalHelper.Cli` 可以在任何支持 .NET 10 的环境（包括服务器、极客桌面与容器）中运行。

### 命令行选项

| 参数 | 说明 |
|---|---|
| *(无参数)* | 启动交互式彩色 ANSI 终端实时仪表盘（按 `Q` 退出） |
| `--status` | 打印单行设备状态摘要后退出（例如 `Cpu: 58°C (22%) \| Gpu: 50°C (12%)`） |
| `--json` | 打印结构化 JSON 数据后退出，包含设备列表、温度、极值与健康度 |
| `--daemon` | 以静默后台守护进程运行，持续监测并执行降温策略 |
| `--mock` | 使用仿真硬件数据（适合开发调试与 CI 测试） |
| `-h`, `--help` | 查看帮助说明 |

### Linux 桌面状态栏集成 (Waybar / Polybar / i3blocks)

配合 `--json` 选项，可以极简集成到 Linux 状态栏中：

```json
// ~/.config/waybar/config
"custom/thermal": {
    "exec": "/usr/local/bin/LaptopThermalHelper.Cli --json",
    "return-type": "json",
    "interval": 2,
    "format": "🔥 {percentage}%"
}
```

### Linux Systemd 守护进程服务配置

创建 `/etc/systemd/system/laptop-thermal.service`：

```ini
[Unit]
Description=Laptop Thermal Helper Daemon
After=multi-user.target

[Service]
Type=simple
ExecStart=/opt/laptop_t_helper/dist_linux/LaptopThermalHelper.Cli --daemon
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

启动并开机自启：
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now laptop-thermal.service
```

> **提示（关于 Linux 调频权限）**：读取 `/sys/class/hwmon` 温度普通用户即可；如需在 Linux 下允许非 root 运行自动降温写入 `no_turbo`，可添加一条 udev 规则赋予权限：
> `echo 'SUBSYSTEM=="cpu", ATTR{intel_pstate/no_turbo}="0", MODE="0666"' | sudo tee /etc/udev/rules.d/99-laptop-thermal.rules`

---

## 温度状态说明

以下为应用的通用保守参考阈值：

| 硬件类别 | 偏高 | 过高 | 严重过热 |
|---|---:|---:|---:|
| CPU | 85°C | 95°C | 100°C |
| GPU 核心温度 | 80°C | 87°C | 92°C |
| NVMe/SATA 固态硬盘 | 60°C | 70°C | 80°C |

---

## 开发者指南

### 项目结构

```text
laptop_t_helper/
├── src/
│   ├── LaptopThermalHelper.App/              # Windows WPF 桌面应用 (UI、托盘、悬浮窗、MVVM)
│   ├── LaptopThermalHelper.Cli/              # 跨平台终端应用与后台守护进程 (TUI/Daemon)
│   ├── LaptopThermalHelper.Application/      # 监控协调器、快照生成与历史追踪 (纯 net10.0)
│   ├── LaptopThermalHelper.Core/             # 领域模型、温度阈值、状态机与统计 (纯 net10.0)
│   ├── LaptopThermalHelper.Hardware.Lhm/     # Windows LibreHardwareMonitor 驱动适配
│   ├── LaptopThermalHelper.Hardware.Linux/   # Linux Sysfs (hwmon + thermal + CPUFreq) 适配
│   ├── LaptopThermalHelper.Hardware.Mac/     # macOS sysctl 与 SMC 状态适配
│   └── LaptopThermalHelper.Infrastructure/    # 本地 CSV 历史、系统信息等基础设施
├── tests/                                    # xUnit 全量自动化测试套件 (共 116 项)
│   ├── LaptopThermalHelper.Core.Tests/
│   ├── LaptopThermalHelper.Application.Tests/
│   ├── LaptopThermalHelper.Infrastructure.Tests/
│   ├── LaptopThermalHelper.Hardware.Lhm.Tests/
│   ├── LaptopThermalHelper.App.Tests/
│   ├── LaptopThermalHelper.Hardware.Linux.Tests/
│   └── LaptopThermalHelper.Cli.Tests/
├── docs/                                     # 应用图标与设计资源
├── LibreHardwareMonitor/                     # 上游只读硬件监视库子模块
├── LICENSES/                                 # 第三方许可证声明
├── build.bat                                 # Windows 一键构建、测试与发布脚本
└── build.sh                                  # Linux / macOS 一键构建、测试与发布脚本
```

### 测试运行

当前项目拥有 **116** 项自动化单元测试，覆盖核心算法、状态机、多平台适配与终端渲染：

```powershell
# 运行全量测试
dotnet test .\LaptopThermalHelper.sln --nologo
```

---

## 致谢

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)：Windows 硬件底层只读驱动支持。
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)、[LiveCharts2](https://github.com/beto-rodriguez/LiveCharts2)、[Serilog](https://serilog.net/)：现代桌面 MVVM、图表与日志支持。
- [.NET](https://dotnet.microsoft.com/) / [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/) / [xUnit](https://xunit.net/)：跨平台运行环境与测试框架。

---

## 许可证

本项目原创代码基于 [MIT License](./LICENSE) 开源发布。上游组件依赖与许可证声明见 [THIRD-PARTY-NOTICES.md](./LICENSES/THIRD-PARTY-NOTICES.md)。

---

## 免责声明

笔记本温控助手是独立第三方开源项目，与 Lenovo、LEGION、Intel、AMD、NVIDIA、Apple、Microsoft 及其他硬件或软件厂商不存在官方隶属、授权或背书关系。产品与商标名称仅用于说明硬件兼容性，归各自权利人所有。

温度、负载、功耗与风扇读数受设备固件与驱动影响；自动降温为可恢复的处理器电源/调频管理辅助功能，请结合设备厂商规格合理使用并在必要时备份重要数据。
