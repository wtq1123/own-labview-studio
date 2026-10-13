# Own LabVIEW Studio

LabVIEW 2024 32 位的自有 VI / 类 / 接口 / 工程生成与副本编辑应用，包含 Windows 桌面入口和 Codex MCP。详见 [应用文档](app/README.md)、[使用指南](app/QUICKSTART.md) 和 [约定范围验收](reports/completion-acceptance-20261013.md)。

进入 `app/` 执行 `build.ps1`，或使用随附的 x86 程序。需要本机 NI LabVIEW 开发环境；EXE / DLL / 源码发布构建另需 Application Builder。NI 安装文件不在本仓库。

运行入口：`app/OpenStudio.cmd`；MCP：`app/bin/OwnLabViewMcp.exe --mcp`。示例配置中的绝对路径须按安装位置调整。

当前目录为明确选取的发布树。生成项目、历史失败产物、原工程及回滚备份保留在原工作区。报告中的绝对路径指原验收机器，不能把原机器通过状态自动当作新环境验证。运行时助手文件由 SHA 清单固定，完整构建/验收脚本在 `app/tests/`。

第三方 MIT 许可位于 `app/licenses/`。自有代码保留权利，本私有仓库未另行授予开源许可。
