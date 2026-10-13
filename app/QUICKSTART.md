# Own LabVIEW Studio 使用指南

适用环境：LabVIEW 2024 32 位。后台生成不需要操作 LabVIEW 窗口。新电脑须按 [部署说明](DEPLOYMENT.md) 安装并配置；lvkit 是独立读取工具，不能假定它已在新环境安装。

## 通过 Codex 使用

直接描述目标，并注明“使用 own-labview，仅生成新副本”。例如：

> 创建一个参数类，包含增益、启用状态、说明文字、采样点数组；生成动态读写访问器，再创建继承它的子类。只写新目录，不运行采集硬件。

> 将我提供的两个自有 VI 合并成新副本；同名控件给复制项加来源前缀，列出名称对应，保留源文件。

> 为生成的类、typedef 和普通 VI 创建工程，再复制工程并调整成员列表。

`own-labview` 负责生成、修改副本及检查；`lvkit` 用于读取/检索 VI。编译、类型和运行判断以 NI 原生回读为准。若当前会话未加载 own-labview 工具，可由 Codex 调用同一应用的 `bin/OwnLabViewMcp.exe --call`，无需切换回旧工具。

“创建成功”只表示本次报告声明的创建/保存/静态检查成功。真实行为需要运行新建的有限测试 VI；测量或硬件程序须单独制定验收。

## 可直接复现的动态类示例

在 Windows PowerShell 中执行：

```powershell
& 'D:\OwnLabVIEWStudio\app\examples\Build-DynamicClassDemo.ps1'
```

脚本生成加法方法体、乘法方法体、父类 `Compute` 动态方法、子类重写和一个包含子类的工程。每次生成独立目录，子类包含独立的父类快照。最后输出 `project_path` 与 `requests_and_reports`；后者保存全部请求、返回结果和 `demo-result.json`。

脚本只生成，不运行产物。父类/子类动态分派的实际运行已通过专用测试，详见最新验收报告。

## 自定义方法参数

先调用 `build_recipe` 生成带连接器的普通 VI，将返回的 `vi_path` 填入：

```json
{
  "name": "Calculator",
  "kind": "class",
  "controls": [],
  "methods": [
    {"name": "Compute", "source_path": "方法体的绝对路径", "dispatch": "dynamic"}
  ]
}
```

提交给 `create_type_artifact`。子类指定 `parent_class`，并在 `overrides` 中填入 `{"method":"Compute.vi","source_path":"子类方法体路径"}`。名称、方向和原生类型必须与父方法匹配；系统会拒绝不匹配的重写。可用 `dispatch:"static"` 创建静态方法。

方法体目前须来自本应用、已编译、已分配连接器且只有 NI 或自有普通子 VI 静态依赖；子 VI 闭包会复制并重新链接。生成的对象端口保持对象直通，业务数据由方法体处理。访问私有字段的逻辑需使用访问器等明确构建，不能仅在方法名称里描述需求。

## 桌面入口及输出

用户可自行双击 `OpenStudio.cmd`。桌面和 Codex 共用后台；此次按要求未操作窗口，未声明桌面外观和交互已验收。

- `projects/`：新建 VI、类、控件和工程，以及同目录报告。
- `examples/complex-class.json`：复杂私有字段定义。
- `recipes/`：可选的图形生成配方。
- `../reports/completion-acceptance-20261013.md`：最新功能与迁移验收。

已提供真实接口继承和实现、多维及簇数组字段、嵌套库、工程虚拟目录和构建规范。构建规范支持 EXE、DLL、源码发布包的创建、复制、修改与实际构建，参数见 README。本机 My Computer 为约定范围，RT / FPGA 明确不支持。验收与备份状态见最新回执。
