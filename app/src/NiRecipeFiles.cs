using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OwnLabView {
// Managed recipe hook: no lease acquisition, worker launch or source Run/Save.
// The recipe owner checks symbolic ref lifetime before resolving diagram IDs.
public static class NiRecipeFiles {
    public static object Validate(Dictionary<string,object> args) {
        Data.Keys(args,"source_path","category","owner_diagram_id","x","y","global_name","write");
        string path=ViImport.CanonicalLocalPath(Data.Text(args,"source_path"));
        if(Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)){TypeImport.ValidateClassPath(path);CheckCategory(args,"class");CheckCoordinates(args);return new{valid=true,source_path=path,source_sha256=Data.Hash(path),category="class",labview_called=false,parent_metadata_unexposed=true};}
        if(ClassSnapshot.HasReport(path)){var member=TypeImport.ValidateSnapshotMember(path);CheckCategory(args,member.Kind);CheckCoordinates(args);return new{valid=true,source_path=member.Path,source_sha256=member.Hash,category=member.Kind,class_snapshot_root=member.ClassSnapshotRoot,labview_called=false};}
        if(TypeImport.IsPrepared(path)) {var prepared=TypeImport.ValidatePrepared(path);CheckCategory(args,prepared.Kind);CheckCoordinates(args);return new{valid=true,source_path=prepared.Path,source_sha256=prepared.Hash,category=prepared.Kind,labview_called=false};}
        if(TypeImport.IsNativeFile(path)) {Storage.NoReparse(path);CheckCoordinates(args);if(args.ContainsKey("category")&&!new[]{"control","global","subvi","polymorphic"}.Contains(Data.Text(args,"category")))throw new ArgumentException("Unsupported declared NI file category.");if(args.ContainsKey("write")&&!(args["write"] is bool))throw new ArgumentException("write must be Boolean.");return new{valid=true,source_path=path,source_sha256=Data.Hash(path),labview_called=false,actual_kind_verified=false};}
        if(args.ContainsKey("global_name")||args.ContainsKey("write"))throw new ArgumentException("Global field/direction options require a prepared global file.");
        return NiFileObjects.ValidateRecipeSource(args);
    }
    public static Dictionary<string,object> Place(object app,string target,Dictionary<string,object> resolvedArgs,List<object> refs,Dictionary<string,string> loadedPaths) {
        Validate(resolvedArgs);
        string path=ViImport.CanonicalLocalPath(Data.Text(resolvedArgs,"source_path"));
        if(Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase))return PlaceClass(app,target,path,resolvedArgs,refs,loadedPaths);
        if(ClassSnapshot.HasReport(path))return PlaceSnapshotMember(app,target,TypeImport.ValidateSnapshotMember(path),resolvedArgs,refs,loadedPaths);
        if(TypeImport.IsPrepared(path))return PlacePrepared(app,target,TypeImport.ValidatePrepared(path),resolvedArgs,refs,loadedPaths);
        if(TypeImport.IsNativeFile(path))return PlaceNative(app,target,path,resolvedArgs,refs,loadedPaths);
        return NiFileObjects.PlaceRecipe(app,target,resolvedArgs,refs,loadedPaths);
    }
    // Caller passes actual saved NI callee paths only; it continues to audit
    // app-generated dependencies using the existing Recipe traversal.
    public static object AuditSaved(object app,string target,IEnumerable<string> sourcePaths,IEnumerable<string> actualNiPaths,List<object> refs,Dictionary<string,string> loadedPaths) {
        var rows=new List<object>();using(var native=new TypeImport.Session(app,refs)) {
            var closure=native.Audit(actualNiPaths);
            foreach(string path in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase)) {
                if(Path.GetExtension(path).Equals(".lvclass",StringComparison.OrdinalIgnoreCase)) {
                    var type=native.ClassMetadata(path);rows.Add(new{source_path=path,source_sha256=type["sha256"],category="class",class_type_dependency=type,native_dependency_closure=native.Audit((string[])type["native_callees"]),source_files_unchanged=true,source_saved=false,source_executed=false});
                } else if(TypeImport.IsPrepared(path)||ClassSnapshot.HasReport(path)) {
                    var prepared=ClassSnapshot.HasReport(path)?TypeImport.ValidateSnapshotMember(path):TypeImport.ValidatePrepared(path);if(prepared.ClassSnapshotRoot!=null)native.ClassMetadata(prepared.ClassSnapshotRoot);object source=native.Open(prepared.Path);if(native.Type(source)!=prepared.VIType)throw new InvalidOperationException("Prepared native VIType changed.");
                    rows.Add(new{source_path=prepared.Path,source_sha256=prepared.Hash,category=prepared.Kind,report_path=prepared.ReportPath,report_sha256=prepared.ReportHash,native_dependency_closure=native.Audit(TypeImport.Session.Paths(source)),source_files_unchanged=true,source_saved=false,source_executed=false});prepared.Verify();
                } else if(TypeImport.IsNativeFile(path)) {
                    string hash=Data.Hash(path);object source=native.Open(path);int type=native.Type(source);string category=TypeImport.Kind(type);
                    rows.Add(new{source_path=path,source_sha256=hash,category,vi_type=type,native_dependency_closure=native.Audit(TypeImport.Session.Paths(source)),retained_ni_source=true,source_files_unchanged=Data.Hash(path)==hash,source_saved=false,source_executed=false});if(Data.Hash(path)!=hash)throw new IOException("Retained NI source changed during saved audit.");
                } else {
                    var validated=Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(NiFileObjects.ValidateRecipeSource(new Dictionary<string,object>{{"source_path",path}}))));
                    string category=Data.Text(validated,"category");object type=category=="class"?NiFileObjects.KnownClassMetadata(app,path,refs):null;
                    object[] sourceClosure=category=="class"?new object[0]:native.Audit(TypeImport.Session.Paths(native.Open(path)));
                    rows.Add(new{source_path=path,source_sha256=validated["source_sha256"],category,native_dependency_closure=sourceClosure,class_type_dependency=type,source_files_unchanged=true,source_saved=false,source_executed=false});
                }
            }
            native.RecordHelpers(loadedPaths);return new{checked_sources=rows.ToArray(),saved_dependency_closure=closure,sources_executed=false,sources_saved=false};
        }
    }
    static void CheckCategory(Dictionary<string,object> args,string kind) {if(args.ContainsKey("category")&&Data.Text(args,"category")!=kind)throw new ArgumentException("category differs from prepared native kind.");if(kind!="global"&&(args.ContainsKey("global_name")||args.ContainsKey("write")))throw new ArgumentException("Only global files accept field/direction options.");if(args.ContainsKey("write")&&!(args["write"] is bool))throw new ArgumentException("write must be Boolean.");}
    static short Coordinate(Dictionary<string,object> args,string key) {if(!args.ContainsKey(key))throw new ArgumentException(key+" is required.");object value=args[key];if(!(value is short||value is int||value is long)||Convert.ToInt64(value)<Int16.MinValue||Convert.ToInt64(value)>Int16.MaxValue)throw new ArgumentException(key+" must be Int16.");return Convert.ToInt16(value);}
    static void CheckCoordinates(Dictionary<string,object> args){Coordinate(args,"x");Coordinate(args,"y");}
    static uint[] Ids(object value){var sequence=value as IEnumerable;if(sequence==null||value is string)throw new InvalidDataException("Native ref array required.");var ids=sequence.Cast<object>().Select(Convert.ToUInt32).ToArray();if(ids.Any(id=>id==0)||ids.Distinct().Count()!=ids.Length)throw new InvalidOperationException("Null or duplicate native ref.");return ids;}
    static string[] Members(TypeImport.Session native,string target,uint diagram,out uint[] rawRefs) {
        rawRefs=Ids(native.Call("Native_GetDiagramObjects",new Dictionary<string,object>{{"diagram_id",diagram}},"object_ids")["object_ids"]);
        var identities=rawRefs.Select(id=>{var identity=native.Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");return Recipe.ObjectIdentityKey(target,Convert.ToInt32(identity["uid"]),Convert.ToString(identity["vi_path"]));}).ToArray();
        if(identities.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=identities.Length)throw new InvalidOperationException("Duplicate physical Diagram member identity.");return identities;
    }
    static void Membership(string[] before,string[] after,string added) {if(before.Contains(added,StringComparer.OrdinalIgnoreCase)||!after.Contains(added,StringComparer.OrdinalIgnoreCase)||before.Except(after,StringComparer.OrdinalIgnoreCase).Any())throw new InvalidOperationException("Placement did not preserve the requested Diagram's physical members: "+Data.Json.Serialize(new{before,after,added_identity=added}));}
    static Dictionary<string,object> PlaceClass(object app,string target,string path,Dictionary<string,object> args,List<object> refs,Dictionary<string,string> loadedPaths) {
        uint diagram=Convert.ToUInt32(args["owner_diagram_id"]);if(app==null||refs==null||diagram==0)throw new ArgumentException("A live owned recipe diagram/session is required.");string hash=Data.Hash(path);
        using(var native=new TypeImport.Session(app,refs)) {
            object targetVi=native.Open(target);var owner=native.Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",diagram}},"uid","vi_path");Recipe.ObjectIdentityKey(target,Convert.ToInt32(owner["uid"]),Convert.ToString(owner["vi_path"]));
            uint[] beforeRefs,afterRefs;var before=Members(native,target,diagram,out beforeRefs);var type=native.ClassMetadata(path);var closure=native.Audit((string[])type["native_callees"]);
            var output=native.Call("Native_AddConstantFromTypedef",new Dictionary<string,object>{{"owner_diagram_id",diagram},{"typedef_path",path},{"x",Coordinate(args,"x")},{"y",Coordinate(args,"y")}},"constant_id","terminal_id");uint id=Convert.ToUInt32(output["constant_id"]),term=Convert.ToUInt32(output["terminal_id"]);if(id==0||term==0)throw new InvalidOperationException("Class placement returned a null ref.");
            var info=native.Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name");if(Convert.ToString(info["class_name"])!="LabVIEWClassConstant")throw new InvalidOperationException("Placed object is not a native class constant.");
            var identity=native.Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");string key=Recipe.ObjectIdentityKey(target,Convert.ToInt32(identity["uid"]),Convert.ToString(identity["vi_path"]));
            var after=Members(native,target,diagram,out afterRefs);Membership(before,after,key);
            var context=native.Call("Own20261012_GetTerminalContext",new Dictionary<string,object>{{"terminal_id",term}},"diagram_id","is_source");if(Convert.ToUInt32(context["diagram_id"])!=diagram||!Convert.ToBoolean(context["is_source"]))throw new InvalidOperationException("Class constant terminal owner/direction differs.");
            // Wire descriptor type0x70 is observed in the independently accepted
            // NI class receipt (0x4070); it is not a VI Server class enum value.
            var descriptor=((IEnumerable)native.Call("Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",term}},"type_string")["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();if(descriptor.Length<2||(descriptor[1]&255)!=0x70)throw new InvalidOperationException("Class constant lacks an actual native class wire descriptor: "+Data.Json.Serialize(descriptor));
            var name=native.Call("Native_GetTerminalName",new Dictionary<string,object>{{"terminal_id",term}},"terminal_name");
            output["object_id"]=id;output["terminal_ids"]=new[]{term};output["actual_class"]="LabVIEWClassConstant";output["identity"]=key;output["uid"]=identity["uid"];output["owner_diagram_id"]=diagram;output["category"]="class";
            output["terminals"]=new object[]{new{terminal_id=term,name=name["terminal_name"],type_descriptor=descriptor,is_source=true,diagram_id=diagram}};output["type_descriptor"]=descriptor;
            output["source_path"]=path;output["source_sha256"]=hash;output["class_type_dependency"]=type;output["source_dependency_closure"]=closure;output["source_files_unchanged"]=true;output["source_saved"]=false;output["source_executed"]=false;
            output["diagram_membership"]=new{before,after,added_identity=key,raw_created_ref_found=afterRefs.Contains(id),raw_previous_refs_preserved=!beforeRefs.Except(afterRefs).Any(),physical_identity_verified=true};
            native.RecordHelpers(loadedPaths);output["helper_paths"]=new Dictionary<string,string>(native.HelperPaths);if(Data.Hash(path)!=hash)throw new IOException("Class source changed during placement.");GC.KeepAlive(targetVi);return output;
        }
    }
    static Dictionary<string,object> PlacePrepared(object app,string target,TypeImport.Prepared prepared,Dictionary<string,object> args,List<object> refs,Dictionary<string,string> loadedPaths) {
        uint diagram=Convert.ToUInt32(args["owner_diagram_id"]);if(app==null||refs==null||diagram==0)throw new ArgumentException("A live owned recipe diagram/session is required.");
        short x=Coordinate(args,"x"),y=Coordinate(args,"y");prepared.Verify();
        using(var native=new TypeImport.Session(app,refs)) {
            object targetVi=native.Open(target);var identity=native.Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",diagram}},"uid","vi_path");Recipe.ObjectIdentityKey(target,Convert.ToInt32(identity["uid"]),Convert.ToString(identity["vi_path"]));
            uint[] beforeRefs,afterRefs;var before=Members(native,target,diagram,out beforeRefs);
            object source=native.Open(prepared.Path);if(native.Type(source)!=prepared.VIType)throw new InvalidOperationException("Prepared actual kind changed before placement.");var sourceClosure=native.Audit(TypeImport.Session.Paths(source));
            Dictionary<string,object> output;uint[] terms;uint id;
            if(prepared.Kind=="control") {
                output=native.Call("Native_AddConstantFromTypedef",new Dictionary<string,object>{{"owner_diagram_id",diagram},{"typedef_path",prepared.Path},{"x",x},{"y",y}},"constant_id","terminal_id");id=Convert.ToUInt32(output["constant_id"]);terms=new[]{Convert.ToUInt32(output["terminal_id"])};
            } else if(prepared.Kind=="global") {
                var labels=Data.Array(prepared.Report["controls"]).Select(Data.Map).Select(row=>Data.Text(row,"label")).ToArray();string field=args.ContainsKey("global_name")?Data.Text(args,"global_name"):labels.FirstOrDefault();
                if(String.IsNullOrWhiteSpace(field)||labels.Count(label=>label==field)!=1)throw new ArgumentException("Global field must select an exact unique prepared label.");bool write=args.ContainsKey("write")&&(bool)args["write"];
                output=native.Call("Native_AddGlobalInDiagram",new Dictionary<string,object>{{"target_path",target},{"owner_diagram_id",diagram},{"subvi_path",prepared.Path},{"x",x},{"y",y}},"object_id");id=Convert.ToUInt32(output["object_id"]);
                native.Call("Native_SetGlobalControlName",new Dictionary<string,object>{{"global_id",id},{"control_name",field}});native.Call("Native_SetGlobalWrite",new Dictionary<string,object>{{"global_id",id},{"write",write}});
                if(Convert.ToString(native.Call("Native_GetGlobalControlName",new Dictionary<string,object>{{"global_id",id}},"control_name")["control_name"])!=field)throw new InvalidOperationException("Global field readback differs.");
                terms=Ids(native.Call("Native_GetNodeTerminals",new Dictionary<string,object>{{"node_id",id}},"terminal_ids")["terminal_ids"]);output["control_name"]=field;output["write"]=write;
            } else {
                output=native.Call("Native_AddSubVIInDiagram",new Dictionary<string,object>{{"target_path",target},{"owner_diagram_id",diagram},{"subvi_path",prepared.Path},{"x",x},{"y",y}},"object_id","terminal_ids");id=Convert.ToUInt32(output["object_id"]);terms=Ids(output["terminal_ids"]);
            }
            if(id==0||terms.Any(term=>term==0))throw new InvalidOperationException("Placement returned a null object/terminal.");
            var info=native.Call("Native_GetObjectInfo",new Dictionary<string,object>{{"object_id",id}},"class_name");string actual=Convert.ToString(info["class_name"]);
            if(prepared.Kind=="global"&&actual!="Global"||prepared.Kind=="polymorphic"&&actual!="PolymorphicSubVI"||prepared.Kind=="subvi"&&actual!="SubVI"||prepared.Kind=="control"&&actual.IndexOf("Constant",StringComparison.Ordinal)<0)throw new InvalidOperationException("Placed native object does not match prepared kind: "+actual);
            var objectIdentity=native.Call("Own20261012_GetObjectIdentity",new Dictionary<string,object>{{"object_id",id}},"uid","vi_path");string key=Recipe.ObjectIdentityKey(target,Convert.ToInt32(objectIdentity["uid"]),Convert.ToString(objectIdentity["vi_path"]));
            var after=Members(native,target,diagram,out afterRefs);Membership(before,after,key);
            var terminals=new List<object>();foreach(uint term in terms) {
                var context=native.Call("Own20261012_GetTerminalContext",new Dictionary<string,object>{{"terminal_id",term}},"diagram_id","is_source");if(Convert.ToUInt32(context["diagram_id"])!=diagram)throw new InvalidOperationException("Placed terminal belongs to another diagram.");
                var type=native.Call("Native_GetTerminalTypeDescriptor",new Dictionary<string,object>{{"terminal_id",term}},"type_string");var name=native.Call("Native_GetTerminalName",new Dictionary<string,object>{{"terminal_id",term}},"terminal_name");
                var descriptor=((IEnumerable)type["type_string"]).Cast<object>().Select(Convert.ToInt32).ToArray();if(descriptor.Length<2)throw new InvalidOperationException("Native terminal descriptor is missing.");
                if(prepared.Kind=="global"&&Convert.ToBoolean(context["is_source"])==(args.ContainsKey("write")&&(bool)args["write"]))throw new InvalidOperationException("Global direction differs.");
                terminals.Add(new{terminal_id=term,name=name["terminal_name"],type_descriptor=descriptor,is_source=context["is_source"],diagram_id=diagram});
            }
            output["object_id"]=id;output["terminal_ids"]=terms;output["terminals"]=terminals.ToArray();output["actual_class"]=actual;output["identity"]=key;output["uid"]=objectIdentity["uid"];output["owner_diagram_id"]=diagram;output["category"]=prepared.Kind;
            output["source_path"]=prepared.Path;output["source_sha256"]=prepared.Hash;output["source_dependency_closure"]=sourceClosure;output["prepared_report_path"]=prepared.ReportPath;output["prepared_report_sha256"]=prepared.ReportHash;
            output["retained_ni_source"]=prepared.RetainedNiSource;
            output["source_files_unchanged"]=true;output["source_saved"]=false;output["source_executed"]=false;output["class_type_dependency"]=null;
            output["diagram_membership"]=new{before,after,added_identity=key,raw_created_ref_found=afterRefs.Contains(id),raw_previous_refs_preserved=!beforeRefs.Except(afterRefs).Any(),physical_identity_verified=true};
            native.RecordHelpers(loadedPaths);output["helper_paths"]=new Dictionary<string,string>(native.HelperPaths);prepared.Verify();GC.KeepAlive(targetVi);return output;
        }
    }
    static Dictionary<string,object> PlaceNative(object app,string target,string path,Dictionary<string,object> args,List<object> refs,Dictionary<string,string> loadedPaths) {
        string hash=Data.Hash(path);using(var native=new TypeImport.Session(app,refs)) {
            object source=native.Open(path);int type=native.Type(source);string kind=TypeImport.Kind(type);CheckCategory(args,kind);
            if(kind=="control"&&!Path.GetExtension(path).Equals(".ctl",StringComparison.OrdinalIgnoreCase)||kind!="control"&&!Path.GetExtension(path).Equals(".vi",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Retained NI file extension and actual kind disagree.");
            if(Convert.ToInt32(LabView.Invoke(source,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Selected NI file must be compiled idle before placement.");
            native.Audit(TypeImport.Session.Paths(source));var controls=kind=="control"||kind=="global"?native.Controls(path):new object[0];
            var prepared=new TypeImport.Prepared{Path=path,Hash=hash,Kind=kind,VIType=type,RetainedNiSource=true,Report=new Dictionary<string,object>{{"controls",controls}}};
            var output=PlacePrepared(app,target,prepared,args,refs,loadedPaths);native.RecordHelpers(loadedPaths);prepared.Verify();return output;
        }
    }
    static Dictionary<string,object> PlaceSnapshotMember(object app,string target,TypeImport.Prepared prepared,Dictionary<string,object> args,List<object> refs,Dictionary<string,string> loadedPaths) {
        prepared.Verify();using(var native=new TypeImport.Session(app,refs)) {
            var metadata=native.ClassMetadata(prepared.ClassSnapshotRoot);
            var members=((IEnumerable)metadata["members"]).Cast<object>().Select(value=>Data.Map(Data.Json.DeserializeObject(Data.Json.Serialize(value)))).Where(row=>String.Equals(TypeImport.NativePath(Data.Text(row,"actual_vi_path")),prepared.Path,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(members.Length!=1||Data.Text(members[0],"type_string")!="VI"||Convert.ToInt32(members[0]["vi_type"])!=prepared.VIType)throw new InvalidOperationException("Actual selected class member Path/type changed.");
            object source=native.Open(prepared.Path);if(native.Type(source)!=prepared.VIType)throw new InvalidOperationException("Selected class member actual VIType differs.");
            if((prepared.Kind=="subvi"||prepared.Kind=="polymorphic")&&Convert.ToInt32(LabView.Invoke(source,"ExecState",BindingFlags.GetProperty))!=1)throw new InvalidOperationException("Selected class method must be compiled idle before placement.");
            native.Audit(TypeImport.Session.Paths(source));
            if(prepared.Kind=="global"||prepared.Kind=="control"){prepared.Report=new Dictionary<string,object>(prepared.Report);prepared.Report["controls"]=native.Controls(prepared.Path);}
            var output=PlacePrepared(app,target,prepared,args,refs,loadedPaths);output["class_snapshot_root"]=prepared.ClassSnapshotRoot;output["class_snapshot_member_identity_checked"]=true;native.RecordHelpers(loadedPaths);prepared.Verify();return output;
        }
    }
}
}
