# 笔记本温控助手

面向普通 Windows 用户的 CPU、GPU 与 SSD 温度监控工具。项目按设计图实现中文状态、趋势曲线、阈值预警与安全建议；第一版不会直接读写 EC、修改 BIOS 或强制控制风扇。

## 当前开发范围

- WPF 深色/浅色总览页
- Fake Provider 模拟温度变化
- 温度阈值、延迟升级与迟滞恢复状态机
- 10 分钟固定容量趋势缓存
- LibreHardwareMonitor 只读适配层
- 每 5 秒 CSV 历史记录与合并导出

## 构建

需要 Windows 11 x64 与 .NET 10 SDK：

直接双击仓库根目录的 `build.bat`，或在命令行执行：

```bat
build.bat
```

脚本会自动还原依赖、执行 Release 构建与测试，并将自包含的 Windows x64 程序发布到 `dist_windows`。构建成功后可运行：

```bat
dist_windows\LaptopThermalHelper.App.exe
```

需要读取真实硬件传感器时使用（部分硬件可能需要以管理员身份启动命令行）：

```bat
dist_windows\LaptopThermalHelper.App.exe --real-hardware
```

也可以手动执行开发构建：

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet format --verify-no-changes
```

默认使用模拟数据。传入 `--real-hardware` 可启用 LibreHardwareMonitor 只读采集；部分传感器可能需要管理员权限。

详细进度见 [开发任务计划](./docs/开发任务计划.md)。传入 `--light-theme` 可直接以浅色主题启动，便于视觉验收。

历史数据保存在 `%LocalAppData%/RoamerFly/LaptopThermalHelper/history/`，总览页的“导出温度日志”会把现有日文件合并到 `exports/`，缺失传感器字段保持为空而不会写成 `0`。

## 免责声明

本项目是独立第三方开源项目，与 Lenovo、LEGION、Intel、NVIDIA、Microsoft 或 LibreHardwareMonitor 维护者不存在官方隶属、授权或背书关系。显示的温度和状态属于辅助参考，不等同于厂商硬件极限或保修标准。使用本软件产生的风险由用户自行承担。

## 许可证

原创代码采用 [MIT License](./LICENSE)。LibreHardwareMonitorLib 采用 MPL-2.0，详情见 [第三方组件声明](./LICENSES/THIRD-PARTY-NOTICES.md)。
