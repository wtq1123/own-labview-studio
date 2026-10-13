# 完整约定范围验收 — 2026-10-13

**约定范围验收通过。** 范围为提取的 29 类 VI 生成/编辑合同，以及追加的自有类、接口、本机 My Computer 项目库、目录和 EXE / DLL / 源码发布构建。原版运行许可失效后，按用户确认的方法使用典型和边界示例验收，不宣称穷举 LabVIEW 所有 API。

本轮新增的多维及簇数组字段、普通子 VI 闭包复制、真实接口继承和动态分派、嵌套库、工程目录、构建规范复制和修改均已验证。EXE、DLL 和源码发布包已实际生成；复制后重新构建、输出改名和源字节保留通过。生成 DLL 的两组有限计算返回 5 和 6.5。

| 检查 | 结果 |
|---|---|
| 提取的核心合同 | 29 类证据核对并关闭已知衔接缺口 |
| 当前 MCP / 主自测 / 引用保护 | 63 / 25 / 95 项通过 |
| 工程复制 / 合并策略 | 14 / 137 项通过 |
| 标量、复杂字段、自定义及嵌套方法、接口 | 原生创建及另一客户端重开后的有限运行通过 |
| 多维字段 | 默认值和读写往返检查通过 |
| 混合与嵌套库 | 限定名称、编译、重开及有限加法运行通过 |
| 发布构建 | EXE / DLL / SDIST 创建、复制、修改与实际构建通过 |
| 迁移 | 212 条物理清单校验；已审核依赖未发现旧工程/厂商路径 |
| 旧工程备份 | 3,922 文件逐 SHA-256 一致，文件集合一致 |

使用 19 个 MCP 工具、105 项配方操作。桌面与 MCP 共用后台。按要求未操作窗口、未执行硬件或测量主 VI；桌面视觉交互不在本次验收内。重开指新客户端/工程上下文，不冒充重启 LabVIEW。

旧的 `full-goal-semantic-independent-review.md` 是发现缺口时的历史快照。本回执逐项关联文件路径放置、导入及重复合并、连接器重分配、移动坐标原点的后续通过证据，并加入当前类/接口与工程回归。历史核心证据核对了文件与哈希，没有声称全部在当前二进制重跑。

## 明确边界

- RT/FPGA and hardware/measurement execution are explicitly excluded by the user.
- The accepted contract uses extracted metadata and typical/boundary fixtures; expired vendor licensing prevented a current original-product runtime comparison. Exhaustive NI API parity is not claimed.
- No window operations, desktop visual or interaction acceptance was performed as requested.
- Private type authoring supports DBL/Boolean/String, nested clusters and rectangular rank1..8 scalar/cluster arrays within published size budgets.
- Custom method snapshots support normal own VI and NI static dependencies; own CTL/global/class dependencies inside a method body and ambiguous inherited interface method names are rejected explicitly.
- Merge rejects opaque reference values and multi-pane/FP-decoration inputs; source connector migration during merge is not part of the agreed minimum contract.
- Build specifications use own normal VI members and EXE/DLL/SDIST kinds. Files were actually built; no EXE user-interface launch or hardware deployment occurred.
- The compiled helpers are part of the delivery. Full historical helper-authoring bootstrap is not an added acceptance gate.
- Historical core fixture evidence was hash-checked and reconciled; the complete historical set was not rerun. Current-binary class/project/extension/MCP and relevant policy regressions are separate receipts.

## 文件及清理

JSON 回执记录当前生产文件 SHA-256、每套原生测试和迁移审计，以及旧目录备份核验。程序仅在生产快照匹配时显示验收通过。此验收脚本本身没有删除文件；旧目录清理情况见后续 `legacy-cleanup-20261013.json`。GitHub 上传状态另记。

使用入口为 `app/OpenStudio.cmd` 或 `app/bin/OwnLabViewMcp.exe --mcp`，参数与可复现命令见 `app/README.md` 和 `app/QUICKSTART.md`。
