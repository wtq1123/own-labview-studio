# 本机部署与基础功能迁移

部署目录可以改变；示例使用 `D:\OwnLabVIEWStudio`，独立开源工具放在 `D:\LabVIEWTools`。

1. 克隆本仓库，执行 `app/build.ps1`，双击 `app/OpenStudio.cmd`。Windows .NET Framework 和 LabVIEW 2024 32 位须已安装。
2. 执行 `app/Deploy-Dependencies.ps1 -Python <可用的64位Python3.12绝对路径>`，安装独立的 lvkit 环境并取得固定提交的开源依赖。
3. Codex 配置 `own-labview` 指向 `app/bin/OwnLabViewMcp.exe`，参数 `--mcp`；`lvkit` 指向 `D:\LabVIEWTools\lvkit\.venv\Scripts\lvkit-mcp.exe`。
4. 对 lvkit 设置 `PYTHONUTF8=1`、`PYTHONIOENCODING=utf-8`，避免中文 Windows 上的大型 VI 描述输出发生 GBK 编码错误。
5. 执行 `app/tests/test-mcp.ps1` 和 `app/tests/test-basic-tools.ps1`。只运行专门生成的有限算术测试；不运行测量 VI 或硬件。

## 开源组件

| 项目 | 固定版本或提交 | 本项目中的作用 |
| --- | --- | --- |
| [lvkit](https://github.com/pragmatest-dev/lvkit) | 0.8.7 / `77f1a3d8e24fe92cc99fe920b98999fe5be9596b` | 独立的只读解析、渲染、检索 CLI/MCP；环境与本应用分开 |
| [lv-scripting](https://github.com/erdosmiller/lv-scripting) | `f7a14bd02f2c7c1fb062201657568a2ea6e0b7f6` | 应用内已包含五个固定哈希的 MIT 运行时文件；完整源码另置 tools 目录 |
| [Zuehlke/labview-mcp](https://github.com/Zuehlke/labview-mcp) | `7702e6d42bcd1218ed4227657b3634547d82b636` | 类与接口助手的 MIT 工作流参考；2026 私有服务不是本应用的运行依赖 |

`lv-scripting` 应用内副本的位置和文件身份由 `approved-open-source.json` 固定，不能直接用新的上游文件替换。完整源码克隆不代表安装了 LabVIEW 调色板；本应用通过随附的受审副本调用组件。Zuehlke 的服务器要求 LabVIEW 2026，本机 2024 使用自有 NI provider 适配助手。

lvkit 的解析器可能对部分 VI 资源或链接给出警告。解析结果用于辅助阅读，实际编译、数据类型和执行行为仍以 NI 原生回读与专门运行测试为准。`tests/test-lvkit-deployment.py` 对有限新建 VI 验证真实 stdio 握手、工具发现、读取与源哈希。

## 迁移的底层功能

- `export_vi_html`：独立导出 NI HTML、前面板和框图 PNG；仅连接已启动实例，不保存或执行源 VI，要求新目录或空目录。
- `auto_wire_copy`：把明确选择的 VI 复制到新项目后，连接唯一标签对应的两个顶层对象；支持前面板控件的 0 号端子和 Node 的原生端子序号。拒绝标签歧义、越界、方向错误、已有输入连线、类型不匹配、跨框图和不受审静态依赖。返回新线完整性及独立编译状态，不执行 VI。失败副本保留诊断。
- 配方 `terminal_wire`、`wire_terminals`、`subvi_path`：读取实际连线和子 VI 调用目标，使用本次配方的符号引用，不接受调用方的裸句柄。
- `python/ni_helpers.py`：可信本地脚本的底层兼容调用器，核对助手路径和 SHA；运行前调用方须持有目标 VI 引用。Python/pywin32 仅供此可选脚本接口，不是桌面或 MCP 的生产依赖。

接线请求示例：

```json
{"source_vi":"D:\\tests\\source.vi","name":"WiredCopy","source_object":"Input","source_terminal":0,"sink_object":"Output","sink_terminal":0}
```

只迁移基础能力；未加入双麦克风/双温度模拟采集、厂商商城或测量工程专用生成器。旧目录应在部署验证、调用脚本迁移和备份内容校验后清理。实验源 VI、校准与测量数据始终保留。

原仓库的完整功能回执来自另一台验收机器。部署检查只确认本次实际执行的范围，不能通过重写历史回执把未重测功能标记成当前环境通过。
