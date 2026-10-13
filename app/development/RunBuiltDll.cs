using System;using System.IO;using System.Runtime.InteropServices;using System.Text.RegularExpressions;using OwnLabView;
class RunBuiltDll {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibrary(string path);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi,SetLastError=true)]static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate double Sum(double a,double b);
 static int Main(string[] args){IntPtr module=IntPtr.Zero;try{string path=Path.GetFullPath(args[0]);var match=Regex.Match(File.ReadAllText(Path.ChangeExtension(path,".h")),@"double __cdecl (\w+)\(double A, double B\)");if(!match.Success)throw new IOException("Unexpected finite test DLL signature.");module=LoadLibrary(path);if(module==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());var address=GetProcAddress(module,match.Groups[1].Value);if(address==IntPtr.Zero)throw new IOException("Export absent.");var call=(Sum)Marshal.GetDelegateForFunctionPointer(address,typeof(Sum));double first=call(2,3),second=call(-2.5,9);if(first!=5||second!=6.5)throw new IOException("Built DLL calculation differs.");Storage.WriteNew(args[1],new{passed=true,dll=path,sha256=Data.Hash(path),export=match.Groups[1].Value,results=new[]{first,second},window_operations_used=false});return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(module!=IntPtr.Zero)FreeLibrary(module);}}
}
