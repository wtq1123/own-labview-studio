# 本机部署与底层迁移验证（2026-10-13）

安装入口：`D:\OwnLabVIEWStudio\app\OpenStudio.cmd`；桌面快捷方式 `Own LabVIEW Studio`。

本次只迁移基础功能：独立 HTML 导出、在新副本中按唯一标签接线、连线端点/端子连线/子 VI 目标读取及可信 Python 助手兼容接口。未加入模拟采集或实验工程专用生成器。生产程序仍是 Windows .NET Framework x86，无旧 Builder/Bridge 或厂商 Python 运行依赖。

实际验证：

| 检查 | 结果 |
| --- | --- |
| Windows C# 构建、离线自测 | 25 项通过 |
| 原生 MCP 回归 | 63 项通过，21 个工具 |
| 底层迁移原生检查 | 15 项通过：副本接线、源保护、HTML/PNG、歧义/越界/已有输入/方向/类型拒绝，以及三项符号引用拓扑读取 |
| 旧版已命名 primitive 副本接线 | 新连线验证通过，ExecState=1，源哈希不变 |
| Python 底层适配 | 根框图、连线端点、样式读取通过，目标哈希不变 |
| lv-scripting 应用内组件 | 五个 SHA 匹配，使用开源组件的新加法配方生成且静态检查通过 |
| NI 类 provider | 两个私有字段及 Gain 动态访问器创建通过，不运行生成的方法 |
| lvkit 0.8.7 | 依赖检查、LabVIEW 2024 检测、测量 VI 只读描述、真实 MCP 握手/九个工具发现/有限 VI 读取通过 |
| Windows 桌面入口 | 窗口创建、图计划校验按钮及离线计算按钮检查通过 |

lvkit 使用独立 Python 3.12 x64 环境；开源源码提交和角色见 `app/DEPLOYMENT.md`。中文输出配置为 UTF-8。现有测量 VI 的解析包含上游解析器对部分资源和链接的警告，不能以该输出代替 NI 对复杂类型和行为的验证。

现有分析脚本已改用 `app/python/ni_helpers.py` 和应用内样式目录，避免继续引用旧 Builder。实验主 VI 和 `.lvproj` 的部署前后 SHA-256 一致，未运行测量程序或硬件。

恢复备份位于 `D:\LabVIEWLegacyBackup\20261013`：Builder 3,881 文件、Bridge 41 文件、旧研究目录 8,234 文件。最终审计时前两个目录已从工作区消失，其压缩备份的文件数和可读取内容已核验；研究目录与压缩备份逐文件 SHA 比较一致。永久删除被执行策略阻止，研究目录以可恢复移出方式从活动工作区清理。

详细本机回执位于 `app/tests/`（未发布的测试输出）：`unit-acceptance.json`、`deployment-mcp-native.json`、`basic-tools-acceptance.json`、`primitive-wire-acceptance.json`、`opensource-deployment.json`、`class-deployment.json`、`lvkit-deployment.json`、`desktop-acceptance.json`。压缩备份包含单独的文件清单与核验记录。

本报告只证明表内实际检查。历史完整验收报告仍保留其原机器、原二进制及原功能范围，不为本次部署重新签发全面功能验收。
