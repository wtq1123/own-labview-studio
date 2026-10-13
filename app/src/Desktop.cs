using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OwnLabView {
public sealed class Desktop : Form {
    readonly TextBox editor=new TextBox(),output=new TextBox(),viPath=new TextBox(),recipeEditor=new TextBox(),projectName=new TextBox(),projectPaths=new TextBox(),projectSource=new TextBox(),typeEditor=new TextBox(),mergeEditor=new TextBox();
    readonly Canvas canvas=new Canvas();
    readonly TabControl tabs=new TabControl{Dock=DockStyle.Fill};
    readonly Label status=new Label();
    readonly List<Button> actions=new List<Button>();
    readonly ComboBox niCategory=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=620};
    readonly TextBox niName=new TextBox{Width=620},niFilename=new TextBox{Width=620};
    public Desktop() {
        Text="Own LabVIEW Studio · 自有 VI 工作台";ClientSize=new Size(1160,790);MinimumSize=new Size(950,650);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(241,245,249);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(18)};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,68));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,220));Controls.Add(layout);
        var title=new Label{Text="Own LabVIEW Studio",Font=new Font(Font.FontFamily,23,FontStyle.Bold),Dock=DockStyle.Fill,ForeColor=Color.FromArgb(15,23,42)};layout.Controls.Add(title,0,0);
        status.Text=CompletionStatus.Accepted()?"已通过约定功能与迁移验收；能力详情显示范围与回执。":"本机工作台已就绪；完整功能验收回执须匹配当前文件。";status.Dock=DockStyle.Fill;status.ForeColor=Color.FromArgb(153,70,10);layout.Controls.Add(status,0,1);
        layout.Controls.Add(tabs,0,2);
        var overview=new TabPage("能力与证据"){Padding=new Padding(12)};tabs.TabPages.Add(overview);
        var overviewLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));overview.Controls.Add(overviewLayout);
        var table=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,GridLines=false};table.Columns.Add("能力",290);table.Columns.Add("当前状态",150);table.Columns.Add("证据 / 边界",530);overviewLayout.Controls.Add(table);
        Row(table,"桌面工作台、Codex MCP","可用","本应用 C# 源码 + Windows .NET Framework，无厂商运行时");
        Row(table,"数字图计划校验与离线计算","可用","端口、重复驱动、悬空输入、循环、数值边界严格检查");
        Row(table,"生成基本算术 VI / 实际输出校验","限定范围","输入、加减乘除、输出；五个自编 NI 助手；每次校验哈希和依赖");
        Row(table,"连接 NI LabVIEW、VI 基本信息","可用","诊断只连接已启动实例；读取路径、执行状态和文件哈希");
        Row(table,"常量、数组、簇与循环配方","限定范围","数值与混合默认值、1D/2D 数字／布尔／字符串数组、循环条件输出已实测");
        Row(table,"新建工程 / 编辑工程成员","限定范围","保存为新副本，成员读回、源文件哈希及工程重开已验收");
        Row(table,"跨 VI 合并、类型/全局文件放置","限定范围","标量、二维DBL数组、混合簇合并；已审核NI多态VI/类常量放置和重开通过");
        Row(table,"自有类、接口及嵌套项目库","后台验收","继承、动态分派、复杂字段和复制编辑；详情见当前回执");
        Row(table,"工程目录和 EXE / DLL / 源码发布","后台验收","本机 My Computer；生成规范及实际构建；RT / FPGA 不在范围内");
        Row(table,"迁移与旧目录",CompletionStatus.Accepted()?"验收已通过":"完整回执待验证","完整状态依据当前产物哈希；本机部署与基础迁移验证单独记录");
        var overviewButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};overviewLayout.Controls.Add(overviewButtons,0,1);
        AddButton(overviewButtons,"查看能力详情",()=>Show(Service.Capabilities()));AddButton(overviewButtons,"连接状态",()=>Show(LabView.Status()));AddButton(overviewButtons,"查看使用说明",()=>System.Diagnostics.Process.Start(Path.Combine(Storage.Root,"README.md")));
        var graphPage=new TabPage("图计划与 VI 生成"){Padding=new Padding(8)};tabs.TabPages.Add(graphPage);
        var graphLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};graphLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));graphLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));graphPage.Controls.Add(graphLayout);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};graphLayout.Controls.Add(buttons);
        AddButton(buttons,"读取 JSON",LoadGraph);AddButton(buttons,"校验 / 预览",()=>{var g=Current();canvas.Graph=g;canvas.Invalidate();Show(g.Summary());});
        AddButton(buttons,"离线计算",()=>Show(Current().Evaluate(new Dictionary<string,object>())));AddButton(buttons,"另存计划",()=>Show(Storage.Save(Current())));
        AddButton(buttons,"生成 VI",()=>Build(false));AddButton(buttons,"生成并验证",()=>Build(true));
        var split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Vertical};split.SizeChanged+=(s,e)=>{if(split.Width>500)split.SplitterDistance=(int)(split.Width*0.46);};graphLayout.Controls.Add(split,0,1);
        editor.Multiline=true;editor.AcceptsTab=true;editor.ScrollBars=ScrollBars.Both;editor.WordWrap=false;editor.Dock=DockStyle.Fill;editor.Font=new Font("Consolas",10);editor.Text=Pretty(Service.Sample);split.Panel1.Controls.Add(editor);canvas.Dock=DockStyle.Fill;split.Panel2.Controls.Add(canvas);canvas.Graph=Graph.Parse(Data.Json.DeserializeObject(Service.Sample));
        var inspect=new TabPage("文件信息与导入"){Padding=new Padding(16)};tabs.TabPages.Add(inspect);
        var inspectLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};inspect.Controls.Add(inspectLayout);
        inspectLayout.Controls.Add(new Label{AutoSize=true,Text="读取 VI 信息，或准备已有 VI、控件和 NI 类文件供原生配方使用。"});viPath.Width=880;inspectLayout.Controls.Add(viPath);
        AddButton(inspectLayout,"选择文件",()=>{using(var dialog=new OpenFileDialog{Filter="LabVIEW 文件 (*.vi;*.ctl;*.lvclass)|*.vi;*.ctl;*.lvclass|VI (*.vi)|*.vi|控件 (*.ctl)|*.ctl|NI 类 (*.lvclass)|*.lvclass",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)viPath.Text=dialog.FileName;});
        AddButton(inspectLayout,"读取基本信息",()=>Show(LabView.Inspect(viPath.Text)));
        AddButton(inspectLayout,"导出 HTML 文档",ExportVi);
        inspectLayout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(880,0),Text="导入独立 VI 会创建新的副本，检查编译状态并保存，不运行 VI。支持无子 VI 依赖或仅依赖 NI vi.lib 的文件。"});
        AddButton(inspectLayout,"导入为新副本",ImportVi);
        inspectLayout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(880,0),Text="准备文件会核对实际类型和依赖，并返回可用于配方的路径。可以把对象放入指定的根框图、循环或 Case 分支。"});
        AddButton(inspectLayout,"准备供配方使用",PrepareFile);
        var recipes=new TabPage("原生 VI 配方"){Padding=new Padding(8)};tabs.TabPages.Add(recipes);
        var recipeLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};recipeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));recipeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));recipeLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));recipes.Controls.Add(recipeLayout);
        recipeLayout.Controls.Add(new Label{Text="按明确步骤创建新的 VI，使用符号引用连接对象。此入口只创建和保存，不运行生成的 VI。",Dock=DockStyle.Fill});
        var recipeButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};recipeLayout.Controls.Add(recipeButtons,0,1);
        AddButton(recipeButtons,"读取配方",()=>{using(var dialog=new OpenFileDialog{Filter="原生配方 (*.json)|*.json",InitialDirectory=Path.Combine(Storage.Root,"recipes"),CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK){var text=File.ReadAllText(dialog.FileName);Recipe.Validate(Data.Json.DeserializeObject(text));recipeEditor.Text=Pretty(text);}});
        AddButton(recipeButtons,"查看操作契约",()=>Show(Recipe.Catalog()));AddButton(recipeButtons,"校验配方",()=>Show(Recipe.Validate(Data.Json.DeserializeObject(recipeEditor.Text))));AddButton(recipeButtons,"创建原生 VI",BuildRecipe);
        recipeEditor.Multiline=true;recipeEditor.AcceptsTab=true;recipeEditor.ScrollBars=ScrollBars.Both;recipeEditor.WordWrap=false;recipeEditor.Dock=DockStyle.Fill;recipeEditor.Font=new Font("Consolas",10);recipeEditor.Text=Pretty("{\"schema_version\":1,\"name\":\"RecipeDemo\",\"steps\":[]}");recipeLayout.Controls.Add(recipeEditor,0,2);
        var templateFolder=Path.Combine(Storage.Root,"recipes");
        if(Directory.Exists(templateFolder)) {
            var templates=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=190,Margin=new Padding(8,10,3,3)};
            foreach(string path in Directory.GetFiles(templateFolder,"*.json"))if(!new[]{"operations.json","helper-provenance.json","ni-styles.json"}.Contains(Path.GetFileName(path)))templates.Items.Add(Path.GetFileName(path));
            templates.SelectedIndexChanged+=(s,e)=>{try{recipeEditor.Text=Pretty(File.ReadAllText(Path.Combine(templateFolder,(string)templates.SelectedItem)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}};
            recipeButtons.Controls.Add(templates);
            if(templates.Items.Contains("multiply.json"))templates.SelectedItem="multiply.json";else if(templates.Items.Count>0)templates.SelectedIndex=0;
        }
        var mergePage=new TabPage("合并 VI"){Padding=new Padding(12)};tabs.TabPages.Add(mergePage);
        var mergeLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};mergeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));mergeLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));mergeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));mergePage.Controls.Add(mergeLayout);
        mergeLayout.Controls.Add(new Label{Text="选择本应用已核验的源 VI 和目标 VI，合并结果保存为新副本。重复控件名称会添加来源前缀；原文件保留。",Dock=DockStyle.Fill},0,0);
        mergeEditor.Multiline=true;mergeEditor.AcceptsTab=true;mergeEditor.ScrollBars=ScrollBars.Both;mergeEditor.WordWrap=false;mergeEditor.Dock=DockStyle.Fill;mergeEditor.Font=new Font("Consolas",10);mergeEditor.Text=Pretty("{\"name\":\"MergedVI\",\"source_path\":\"\",\"target_source_path\":\"\",\"dx\":400,\"dy\":0}");mergeLayout.Controls.Add(mergeEditor,0,1);
        var mergeButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};mergeLayout.Controls.Add(mergeButtons,0,2);
        AddButton(mergeButtons,"读取合并配置",()=>{using(var dialog=new OpenFileDialog{Filter="合并配置 (*.json)|*.json",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)mergeEditor.Text=Pretty(File.ReadAllText(dialog.FileName));});
        AddButton(mergeButtons,"合并为新 VI",MergeVi);
        var niPage=new TabPage("NI 文件放置"){Padding=new Padding(16)};tabs.TabPages.Add(niPage);
        var niLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};niPage.Controls.Add(niLayout);
        niLayout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(880,0),Text="创建一个新 VI，放置已审核的 NI 多态子 VI 或类常量，并验证保存后重开结果。"});
        niCategory.Items.AddRange(new object[]{"Little-Endian Bytes To Words — 多态子 VI","Less Comparable — 类常量"});niCategory.SelectedIndex=0;niLayout.Controls.Add(niCategory);
        niLayout.Controls.Add(new Label{AutoSize=true,Text="新项目名称（英文、数字或下划线）"});niName.Text="NiFileExample";niLayout.Controls.Add(niName);
        niLayout.Controls.Add(new Label{AutoSize=true,Text="VI 文件名（可留空，支持中文 .vi 文件名）"});niLayout.Controls.Add(niFilename);
        AddButton(niLayout,"放置并保存新 VI",PlaceNiFile);
        var projects=new TabPage("工程"){Padding=new Padding(12)};tabs.TabPages.Add(projects);
        var projectLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=5,ColumnCount=1};projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));projectLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));projectLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));projects.Controls.Add(projectLayout);
        projectLayout.Controls.Add(new Label{Text="编辑工程成员清单，每行一个本应用生成的 VI 路径。保存到新工程，来源文件保持不变。",Dock=DockStyle.Fill},0,0);
        projectName.Text="MyLabViewProject";projectName.Dock=DockStyle.Fill;projectLayout.Controls.Add(projectName,0,1);
        projectSource.ReadOnly=true;projectSource.Dock=DockStyle.Fill;projectLayout.Controls.Add(projectSource,0,2);
        projectPaths.Multiline=true;projectPaths.ScrollBars=ScrollBars.Both;projectPaths.WordWrap=false;projectPaths.Dock=DockStyle.Fill;projectLayout.Controls.Add(projectPaths,0,3);
        var projectButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};projectLayout.Controls.Add(projectButtons,0,4);
        AddButton(projectButtons,"读取已有工程",()=>{using(var dialog=new OpenFileDialog{Filter="LabVIEW 工程 (*.lvproj)|*.lvproj",InitialDirectory=Path.Combine(Storage.Root,"projects"),CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK){var source=ProjectSource.Validate(dialog.FileName);projectSource.Text=source.Path;projectPaths.Lines=source.MemberPaths;string name=Path.GetFileNameWithoutExtension(source.Path);projectName.Text=(name.Length>33?name.Substring(0,33):name)+"_Edited";}});
        AddButton(projectButtons,"清除来源",()=>projectSource.Text="");
        AddButton(projectButtons,"添加 VI / 控件 / 类 / 库",()=>{using(var dialog=new OpenFileDialog{Filter="LabVIEW 文件 (*.vi;*.ctl;*.lvclass;*.lvlib)|*.vi;*.ctl;*.lvclass;*.lvlib",InitialDirectory=Path.Combine(Storage.Root,"projects"),Multiselect=true,CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)projectPaths.Text=String.Join(Environment.NewLine,projectPaths.Lines.Concat(dialog.FileNames).Where(x=>!String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));});
        AddButton(projectButtons,"检查工程清单",()=>Show(Projects.Validate(ProjectRequest())));AddButton(projectButtons,"保存新工程",BuildProject);
        var typePage=new TabPage("自定义控件与全局变量"){Padding=new Padding(12)};tabs.TabPages.Add(typePage);
        var typeLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1};typeLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));typeLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));typePage.Controls.Add(typeLayout);
        typeEditor.Multiline=true;typeEditor.ScrollBars=ScrollBars.Both;typeEditor.WordWrap=false;typeEditor.Dock=DockStyle.Fill;typeEditor.Font=new Font("Consolas",10);typeEditor.Text=Pretty(TypeArtifacts.Sample);typeLayout.Controls.Add(typeEditor,0,0);
        var typeButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};typeLayout.Controls.Add(typeButtons,0,1);AddButton(typeButtons,"检查控件定义",()=>Show(TypeArtifacts.Validate(Data.Map(Data.Json.DeserializeObject(typeEditor.Text)))));AddButton(typeButtons,"保存新控件 / 全局变量 / 自有类",BuildType);AddButton(typeButtons,"自有类示例",()=>typeEditor.Text=TypeArtifacts.ClassSample);
        var toolsPage=new TabPage("全部后台工具"){Padding=new Padding(12)};tabs.TabPages.Add(toolsPage);
        var toolsLayout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1};toolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));toolsLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));toolsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));toolsPage.Controls.Add(toolsLayout);
        var toolSelector=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};var toolDefinitions=Service.Tools().Select(x=>Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(x)))).ToArray();foreach(var tool in toolDefinitions)toolSelector.Items.Add(Data.Text(tool,"name"));toolSelector.SelectedIndex=0;toolsLayout.Controls.Add(toolSelector,0,0);
        var toolArgs=new TextBox{Multiline=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10),Text="{}"};toolsLayout.Controls.Add(toolArgs,0,1);var toolButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};toolsLayout.Controls.Add(toolButtons,0,2);
        AddButton(toolButtons,"参数说明",()=>Show(toolDefinitions.Single(t=>Data.Text(t,"name")==Convert.ToString(toolSelector.SelectedItem))));
        AddButton(toolButtons,"读取参数 JSON",()=>{using(var dialog=new OpenFileDialog{Filter="JSON 参数 (*.json)|*.json",InitialDirectory=Path.Combine(Storage.Root,"examples"),CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK)toolArgs.Text=Pretty(File.ReadAllText(dialog.FileName));});
        AddButton(toolButtons,"执行工具",()=>RunTool(Convert.ToString(toolSelector.SelectedItem),toolArgs.Text));
        output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Both;output.WordWrap=false;output.Dock=DockStyle.Fill;output.Font=new Font("Consolas",9);output.BackColor=Color.FromArgb(15,23,42);output.ForeColor=Color.FromArgb(226,232,240);layout.Controls.Add(output,0,3);
        Show(new{message="工作台已启动。选择“图计划与 VI 生成”体验加法示例。",complete_replacement=false});
    }
    void Row(ListView list,string a,string b,string c){list.Items.Add(new ListViewItem(new[]{a,b,c}));}
    void AddButton(Control parent,string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=34,Padding=new Padding(6),FlatStyle=FlatStyle.Flat};b.Click+=(s,e)=>{try{action();}catch(Exception ex){Show(new{error=Data.Error(ex)});}};parent.Controls.Add(b);actions.Add(b);}
    Graph Current(){return Graph.Parse(Data.Json.DeserializeObject(editor.Text));}
    async void BuildType(){Dictionary<string,object> request;try{request=Data.Map(Data.Json.DeserializeObject(typeEditor.Text));TypeArtifacts.Validate(request);}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var b in actions)b.Enabled=false;Show(new{message="正在保存新控件 / 全局变量 / 自有类…",run_vi=false});try{Show(await Task.Run(()=>TypeArtifacts.Create(request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var b in actions)b.Enabled=true;}}
    async void ImportVi(){var request=new Dictionary<string,object>{{"source_path",viPath.Text.Trim()},{"name","ImportedVI"}};try{ViImport.Validate(request);}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var b in actions)b.Enabled=false;Show(new{message="正在导入独立 VI 副本…",run_vi=false});try{Show(await Task.Run(()=>ViImport.Import(request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var b in actions)b.Enabled=true;}}
    async void PrepareFile(){var request=new Dictionary<string,object>{{"source_path",viPath.Text.Trim()},{"name","PreparedFile"}};foreach(var button in actions)button.Enabled=false;Show(new{message="正在准备文件并核对类型与依赖…",run_vi=false});try{Show(await Task.Run(()=>Service.Call("prepare_file",request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var button in actions)button.Enabled=true;}}
    async void ExportVi(){string path=viPath.Text;using(var dialog=new FolderBrowserDialog{Description="选择新的或空的目录，保存 VI HTML 和图片"})if(dialog.ShowDialog(this)==DialogResult.OK){var request=new Dictionary<string,object>{{"path",path},{"output_html",Path.Combine(dialog.SelectedPath,"VI.html")}};foreach(var button in actions)button.Enabled=false;try{Show(await Task.Run(()=>BasicViTools.Export(request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var button in actions)button.Enabled=true;}}}
    async void MergeVi(){Dictionary<string,object> request;try{request=Data.Map(Data.Json.DeserializeObject(mergeEditor.Text));}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var button in actions)button.Enabled=false;Show(new{message="正在合并 VI 并保存新副本…",run_generated_vi=false});try{Show(await Task.Run(()=>Service.Call("merge_vi",request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var button in actions)button.Enabled=true;}}
    async void PlaceNiFile(){var request=new Dictionary<string,object>{{"name",niName.Text.Trim()},{"category",niCategory.SelectedIndex==0?"polymorphic":"class"}};if(!String.IsNullOrWhiteSpace(niFilename.Text))request["filename"]=niFilename.Text.Trim();foreach(var button in actions)button.Enabled=false;Show(new{message="正在放置 NI 文件并验证保存与重开…",run_generated_vi=false});try{Show(await Task.Run(()=>Service.Call("place_ni_file",request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var button in actions)button.Enabled=true;}}
    async void RunTool(string tool,string json){Dictionary<string,object> request;try{request=Data.Map(Data.Json.DeserializeObject(json));}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var button in actions)button.Enabled=false;Show(new{message="正在执行所选后台工具…",tool});try{Show(await Task.Run(()=>Service.Call(tool,request)));}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var button in actions)button.Enabled=true;}}
    void Show(object obj){output.Text=Pretty(Data.Json.Serialize(obj));}
    void LoadGraph(){using(var dialog=new OpenFileDialog{Filter="图计划 (*.json)|*.json",CheckFileExists=true})if(dialog.ShowDialog(this)==DialogResult.OK){string text=File.ReadAllText(dialog.FileName);Graph.Parse(Data.Json.DeserializeObject(text));editor.Text=Pretty(text);}}
    async void Build(bool verify){Graph graph;try{graph=Current();}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var b in actions)b.Enabled=false;Show(new{message="正在独立项目目录中生成 VI，请等待…",verify});try{var result=await Task.Run(()=>Native.Build(graph,verify));Show(result);}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var b in actions)b.Enabled=true;}}
    async void BuildRecipe(){object recipe;try{recipe=Data.Json.DeserializeObject(recipeEditor.Text);Recipe.Validate(recipe);}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var b in actions)b.Enabled=false;Show(new{message="正在创建新的原生 VI 配方项目，请等待…",run_generated_vi=false});try{var result=await Task.Run(()=>Recipe.Build(recipe));Show(result);}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var b in actions)b.Enabled=true;}}
    Dictionary<string,object> ProjectRequest(){var request=new Dictionary<string,object>{{"name",projectName.Text.Trim()},{"vi_paths",projectPaths.Lines.Where(x=>!String.IsNullOrWhiteSpace(x)).Select(x=>(object)x.Trim()).ToArray()}};if(!String.IsNullOrWhiteSpace(projectSource.Text))request["source_project"]=projectSource.Text;return request;}
    async void BuildProject(){Dictionary<string,object> request;try{request=ProjectRequest();Projects.Validate(request);}catch(Exception ex){Show(new{error=Data.Error(ex)});return;}foreach(var b in actions)b.Enabled=false;Show(new{message="正在创建新 LabVIEW 工程…",run_generated_vis=false});try{var result=await Task.Run(()=>Projects.Create(request));Show(result);}catch(Exception ex){Show(new{error=Data.Error(ex)});}finally{foreach(var b in actions)b.Enabled=true;}}
    public static string Pretty(string json){var text=new System.Text.StringBuilder();bool quoted=false,escape=false;int indent=0;foreach(char c in json){if(quoted){text.Append(c);if(escape)escape=false;else if(c=='\\')escape=true;else if(c=='\"')quoted=false;continue;}if(c=='\"'){quoted=true;text.Append(c);}else if(c=='{'||c=='['){text.Append(c).AppendLine();indent++;text.Append(' ',indent*2);}else if(c=='}'||c==']'){text.AppendLine();indent--;text.Append(' ',indent*2).Append(c);}else if(c==','){text.Append(c).AppendLine().Append(' ',indent*2);}else if(c==':')text.Append(": ");else if(!Char.IsWhiteSpace(c))text.Append(c);}return text.ToString();}
    public void Smoke(string path){Shown+=(s,e)=>{var timer=new Timer{Interval=700};timer.Tick+=(sender,args)=>{timer.Stop();Screenshot(path);tabs.SelectedIndex=1;actions.First(b=>b.Text=="校验 / 预览").PerformClick();actions.First(b=>b.Text=="离线计算").PerformClick();var result=Data.Map(Data.Json.DeserializeObject(output.Text));bool passed=result.ContainsKey("outputs")&&Data.Number(Data.Map(result["outputs"])["result"])==5;Screenshot(Path.Combine(Path.GetDirectoryName(path),"desktop-graph.png"));File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"desktop-acceptance.json"),Data.Json.Serialize(new{passed,window_created=true,graph_tab_opened=true,validation_button_clicked=true,evaluation_button_clicked=true,result}),new System.Text.UTF8Encoding(false));Close();};timer.Start();};}
    void Screenshot(string path){using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}}
}
public sealed class Canvas:Panel {
    public Graph Graph;
    public Canvas(){DoubleBuffered=true;BackColor=Color.White;AutoScroll=true;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(Graph==null)return;var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform(AutoScrollPosition.X,AutoScrollPosition.Y);var points=new Dictionary<string,RectangleF>();var rows=new Dictionary<int,int>();int max=Graph.Order.Max(n=>n.Level);float gap=180;foreach(var n in Graph.Order){int row=rows.ContainsKey(n.Level)?rows[n.Level]:0;rows[n.Level]=row+1;points[n.Id]=new RectangleF(12+n.Level*gap,35+row*85,112,55);}AutoScrollMinSize=new Size((int)(max*gap+145),rows.Values.Max()*85+65);using(var pen=new Pen(Color.FromArgb(100,116,139),2))foreach(var wire in Graph.Wires){var a=points[wire.From.Split('.')[0]];var b=points[wire.To.Split('.')[0]];g.DrawLine(pen,a.Right,a.Top+27,b.Left,b.Top+27);}foreach(var n in Graph.Order){var r=points[n.Id];using(var brush=new SolidBrush(n.IsMath?Color.FromArgb(219,234,254):Color.FromArgb(220,252,231)))g.FillRectangle(brush,r);using(var pen=new Pen(Color.FromArgb(148,163,184)))g.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);g.DrawString(n.Label,Font,Brushes.Black,r.X+6,r.Y+5);g.DrawString(n.Kind,Font,Brushes.DimGray,r.X+6,r.Y+29);}g.DrawString("计划预览（不是 LabVIEW 框图）",Font,Brushes.DimGray,12,8);}
}
}
