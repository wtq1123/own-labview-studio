using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace OwnLabView {
public static class Program {
    [STAThread] public static int Main(string[] args) {
        try {
            bool gui=args.Length==0||args[0]=="--gui"||args[0]=="--gui-smoke";
            if(!gui)Console.OutputEncoding=new UTF8Encoding(false);
            if(args.Length==1&&args[0]=="--self-test")return Tests.Run();
            if(args.Length==1&&args[0]=="--ni-keepalive")return NiApplicationLifetime.RunHost();
            if(args.Length==1&&args[0]=="--mcp"){Protocol.Run();return 0;}
            if(args.Length==1&&args[0]=="--capabilities"){Console.WriteLine(Data.Json.Serialize(Service.Capabilities()));return 0;}
            if(args.Length==3&&args[0]=="--call"){Console.WriteLine(Data.Json.Serialize(Service.Call(args[1],Data.Map(Data.Json.DeserializeObject(File.ReadAllText(args[2],Encoding.UTF8))))));return 0;}
            if(args.Length==3&&args[0]=="--build-worker"){Console.WriteLine(Data.Json.Serialize(Native.Worker(args[1],args[2]=="verify")));return 0;}
            if(args.Length==2&&args[0]=="--recipe-worker"){Console.WriteLine(Data.Json.Serialize(Recipe.Worker(args[1])));return 0;}
            if(args.Length==2&&args[0]=="--library-worker"){Console.WriteLine(Data.Json.Serialize(Libraries.Worker(args[1])));return 0;}
            if(args.Length==2&&args[0]=="--project-worker"){Console.WriteLine(Data.Json.Serialize(Projects.Worker(args[1])));return 0;}
            if(args.Length==2&&args[0]=="--import-worker"){Console.WriteLine(Data.Json.Serialize(ViImport.Worker(args[1])));return 0;}
            if(args.Length==2&&args[0]=="--merge-worker"){Console.WriteLine(Data.Json.Serialize(ViMerge.Worker(args[1])));return 0;}
            if(args.Length==2&&args[0]=="--type-worker"){Console.WriteLine(Data.Json.Serialize(TypeArtifacts.Worker(args[1])));return 0;}
            if(args.Length==3&&args[0]=="--ni-file-worker"){Console.WriteLine(Data.Json.Serialize(NiFileObjects.Worker(args[1],args[2])));return 0;}
            if(args.Length==2&&args[0]=="--prepare-file-worker"){Console.WriteLine(Data.Json.Serialize(TypeImport.Worker(args[1])));return 0;}
            if(args.Length==2&&args[0]=="--recipe"){Console.WriteLine(Data.Json.Serialize(Recipe.Build(Data.Json.DeserializeObject(File.ReadAllText(args[1],Encoding.UTF8)))));return 0;}
            if(args.Length==2&&args[0]=="--project"){Console.WriteLine(Data.Json.Serialize(Projects.Create(Data.Map(Data.Json.DeserializeObject(File.ReadAllText(args[1],Encoding.UTF8))))));return 0;}
            if(args.Length==3&&args[0]=="--build"){Console.WriteLine(Data.Json.Serialize(Native.Build(Graph.Parse(Data.Json.DeserializeObject(File.ReadAllText(args[1],Encoding.UTF8))),args[2]=="verify")));return 0;}
            if(args.Length==0||args[0]=="--gui"||args[0]=="--gui-smoke") {
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);var form=new Desktop();
                if(args.Length==2&&args[0]=="--gui-smoke")form.Smoke(args[1]);
                Application.Run(form);return 0;
            }
            throw new ArgumentException("Usage: --gui | --mcp | --self-test | --capabilities | --call tool args.json | --build graph.json verify|create | --recipe recipe.json | --project args.json");
        } catch(Exception ex){Console.Error.WriteLine(Data.Error(ex));return 1;}
        finally {NiApplicationLifetime.Close();}
    }
}
}
