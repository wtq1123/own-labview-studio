# Own LabVIEW Studio

Windows 桌面应用和 Codex MCP 共用的 LabVIEW 生成、复制编辑后台。目标环境为 **LabVIEW 2024 32 位 / Windows .NET Framework 4.x**，实际测试版本为 24.1f0。运行不依赖 VI Generator、VIRobotics、viscriptpy、LabVIEWBuilder 或 LabVIEWBridge。

当前交付按已提取的 29 类 VI Generator 合同及用户追加的类、接口、本机工程和发布构建范围验收。使用典型及边界示例，不声称穷举 LabVIEW 全部对象、属性或类型组合。完整记录见 [验收报告](../reports/completion-acceptance-20261013.md)，操作示例见 [使用指南](QUICKSTART.md)。应用状态检查回执与当前生产文件哈希；迁移到新目录或重建后，原机器回执不自动成为新环境的验收证明。

## 启动与构建

用户可双击 `OpenStudio.cmd`。本次开发和验收按要求仅使用后台 COM，未进行窗口自动化、桌面视觉或交互验收。

```powershell
.\build.ps1
.\bin\OwnLabViewMcp.exe --mcp
.\bin\OwnLabViewMcp.exe --call studio_capabilities examples\empty.json
```

`build.ps1` 使用 Windows 自带的 .NET Framework C# 编译器，生成两个 x86 程序并运行离线自测。运行生成操作还需要本机 NI 开发环境和启用的 VI Scripting；发布构建需要可用的 NI Application Builder。Python 只用于部分验收脚本，不是生产运行依赖。

将 `codex-mcp.example.toml` 的 `command` 改为实际绝对路径后配置到 Codex。MCP 是逐行 JSON-RPC / stdio；日志不混入协议输出。当前公开 21 个工具，配方目录有 108 项操作。完整参数以 `tools/list` 和 `recipe_catalog` 返回值为准。首次部署和开源组件安装见 [部署说明](DEPLOYMENT.md)。

## VI 与配方

- `validate_graph`、`evaluate_graph`、`save_graph`、`build_vi` 提供有限算术图入口。
- `validate_recipe`、`build_recipe` 提供节点、控件、常量、结构、连线、循环寄存器、隧道、Formula、命名簇、属性及方法节点、连接器自动分配和重分配等操作。
- `search_styles`、`find_ni_examples` 检索本地样式和 NI 例程；`labview_status`、`inspect_vi` 读取原生状态。
- `export_vi_html` 独立导出 NI HTML 与 PNG，`auto_wire_copy` 对新磁盘副本做一条受检顶层连线；配方新增端子连线、连线端点及子 VI 路径读取。详细约束见部署说明。
- `import_vi`、`prepare_file` 准备独立副本；`source_vi` 编辑自有 VI 副本。`file_place` 将已准备 VI、CTL、全局、类或已审核 NI 多态文件放入当前根/循环/Case 框图。`place_ni_file` 保留审核过的 NI 快捷候选。
- `merge_vi` 在新目标副本中合并实际前面板控件和框图。检查保存默认值、原生类型、几何位置、拓扑和依赖；同名复制控件加来源前缀并返回映射。导入、已复制和已合并产物可以继续组合。

配方使用 `$step.output` 符号引用，拒绝原始整数引用、跨 VI 引用和失效对象。数据类型或结构更改后应重新获取端子。默认生成操作不执行生成的 VI；“保存/编译成功”与行为运行验证分开记录。

## 控件、类与接口

`create_type_artifact` 支持 `kind:control/global/class`；字段声明使用 `controls`。

- 标量字段：DBL、Boolean、String。
- 簇：`children`，默认值按成员顺序给出。
- 数组：`element` 和 `dimensions:1..8`；元素可为标量或嵌套簇，簇可含数组。多维值须为矩形嵌套数组，`[]` 表示空数组。直接数组套数组须改用 `dimensions`。
- 每个数组最多 1024 个叶元素，树最多 128 个对象、深度 8；不是任意大小数据序列化接口。

类的 `accessors` 创建真实静态或动态读写方法；`parent_class` 采用父类及依赖快照。`overrides` 创建重写，原生连接器和动态分派规则会读回核验。

自定义 `methods` 项包含 `name`、`source_path` 和 `dispatch`（默认 `dynamic`，可为 `static`）。方法体必须是已编译、有分配连接器的自有普通 VI，静态依赖可包含 NI 和自有普通子 VI。自有闭包复制后逐个重新链接。子类用 `overrides:[{method:"Compute.vi",source_path:"..."}]` 替换计算体；名称、方向和原生类型必须与父方法一致。对象端口保持直通，访问私有数据应明确构建访问器逻辑。独立 CTL、全局 VI 或类依赖的方法体闭包尚不接受，会报告具体路径。

`kind:class,is_interface:true,controls:[]` 创建真实接口；`interfaces` 用于接口继承或类实现多个接口。接口方法有默认实现，可被类重写。接口无私有字段；多个直接父接口同名方法的歧义会拒绝。

```powershell
.\examples\Build-DynamicClassDemo.ps1
```

该示例生成父类加法方法、子类乘法重写及工程，输出请求和报告路径；不运行测量程序。

## 项目库、工程与发布

`create_library` 用 `member_paths` 接收自有 VI、CTL、全局 VI、类及嵌套库。成员保存为新库内的副本并核对限定名称、编译状态和依赖。`source_library` 复制自有库，`remove_members` 删除副本中的精确直属成员名，`member_paths` 添加新成员副本。

`create_project` 接收 `vi_paths`、`folders`（如 `Logic/Arithmetic`）和 `member_folders`（绝对文件路径到虚拟目录的映射）。`source_project` 创建已有自有工程的副本并替换成员列表，保留目录布局。

`build_specs` 的每项包含 `name`、`kind`（`EXE`、`DLL`、`SDIST`）、`source_path`。EXE / DLL 可设 `output_name`。`build:true` 调用本机 Application Builder 实际构建；默认仅保存规范。复制工程时省略 `build_specs` 保留原规范，显式数组替换规范。所有发布输出写入新工程的 `builds/` 目录，源工程和既有输出保留。

本机 **My Computer** 为约定目标；RT / FPGA 明确不在范围。构建文件成功不等同部署或硬件运行验证。验收中的 DLL 是新生成的有限算术测试，不涉及用户仪器程序。

## 输出与验收

产物和相邻 JSON 报告位于 `projects/`，每次生成唯一目录。报告记录实际 NI 路径、编译状态、文件哈希、依赖及明确执行范围。失败产物保留供诊断，不会伪装成成功。

```powershell
.\tests\test-mcp.ps1
.\tests\test-merge-offline.ps1
.\tests\Run-ClassCompletion.ps1
.\tests\Run-ExtendedCompletion.ps1
```

后两个套件需要 Python 3（标准库即可），可设置环境变量 `OWN_PYTHON` 指向解释器。NI 测试须串行运行。套件只运行专门生成的有限测试 VI，禁止对硬件或测量主 VI 套用通用 Run 验收。

## 边界与来源

不包含通用 Agent For LabVIEW 的联网、SSH、图像生成等工具，这些由 Codex 提供。合并对原生类型和值做实际审核；未知引用值、多 Pane、前面板装饰复制及源连接器迁移不在已验收输入范围。项目和类型入口优先处理自有或审核过的资产，拒绝不完整依赖，不承诺任意外部工程无损导入。

部分自编助手的历史制作使用过厂商编辑器，生产运行不加载厂商组件。保留助手源流转和 SHA 清单；从零重建全部历史助手不是本次验收条件。`approved-open-source.json` 固定 MIT lv-scripting 组件，`licenses/` 保留其许可及 Zuehlke 参考许可。NI 软件由本机安装提供，上传包不含 NI 安装文件。
