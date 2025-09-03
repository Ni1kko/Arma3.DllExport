using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Arma3.DllExport.MsBuild
{
    public class DllExporter
    {
        // Enum to match the one in Arma3.DllExport
        public enum ArmaExport
        {
            RVExtension,
            RVExtensionVersion,
            RVExtensionArgs,
            RVExtensionRegisterCallback,
            RVExtensionContext
        }

        public static string IlasmPath { get; set; }
        public static string IldasmPath { get; set; }

        private bool _injected;

        public bool FoundMethod => ExportMethods.Any();
        public string Target { get; }
        public ModuleDefinition Module { get; }
        public Dictionary<ArmaExport, MethodDefinition> ExportMethods { get; } = new Dictionary<ArmaExport, MethodDefinition>();

        public bool KeepIl { get; set; } = true;
        public Action<string> Log = s => Console.WriteLine(s);

        public bool Debug => Module.HasDebugHeader;
        public CpuPlatform Cpu
        {
            get
            {
                if (Module.Architecture == TargetArchitecture.AMD64)
                    return CpuPlatform.X64;
                if (Module.Architecture == TargetArchitecture.I386 && (Module.Attributes & ModuleAttributes.Required32Bit) != 0)
                    return CpuPlatform.X86;
                return CpuPlatform.AnyCpu;
            }
        }

        public DllExporter(string target)
        {
            Target = target;
            Module = ModuleDefinition.ReadModule(Target, new ReaderParameters { ReadSymbols = true });
            if (Module.Kind != ModuleKind.Dll)
                throw new DllExporterException("Only supports DLLs");
            if (Cpu == CpuPlatform.AnyCpu)
                throw new DllExporterException("AnyCpu DLLs are not supported. Please target x86 or x64.");

            // New Logic: Find ALL methods with the attribute
            foreach (var typeDefinition in Module.Types)
            {
                foreach (var method in typeDefinition.Methods)
                {
                    var attribute = method.CustomAttributes.FirstOrDefault(c => c.AttributeType.Name == "ArmaDllExportAttribute");
                    if (attribute == null) continue;

                    var exportType = (ArmaExport)attribute.ConstructorArguments[0].Value;

                    if (ExportMethods.ContainsKey(exportType))
                    {
                        throw new DllExporterException($"You have multiple methods with the attribute [ArmaDllExport({exportType})]. Only one is allowed per type.");
                    }

                    ValidateMethod(method, exportType);
                    ExportMethods.Add(exportType, method);
                }
            }
        }

        // New Logic: Validate each method based on its specific export type
        private void ValidateMethod(MethodDefinition method, ArmaExport exportType)
        {
            if (!method.IsPublic || !method.IsStatic)
                throw new DllExporterException($"The export method '{method.Name}' for {exportType} must be public and static.");

            // Note: Add more specific signature validation here if you want to be strict.
            // For now, we'll assume the user gets it right, as handling all marshalling cases is complex.
        }


        public void Export()
        {
            if (!FoundMethod)
            {
                Log("No export methods found. Did you forget the [ArmaDllExport] attribute?");
                return;
            }
            if (_injected)
                throw new DllExporterException("You can only inject into a DLL once.");

            Log($"Found {ExportMethods.Count} export(s). Injecting wrappers...");
            InjectWrappers();

            Log("Removing ArmaDllExport attribute and reference");
            RemoveArmaExportRefs();

            Log("Writing injected DLL");
            Module.Write(Target);

            var ilPath = $"{Target}.il";
            Log("Disassembling DLL");
            IlDasm(Target, ilPath);

            Log("Adding exports");
            IlParser(ilPath);

            Log("Assembling DLL");
            IlAsm(ilPath);

            _injected = true;
            if (KeepIl)
                return;
            Log("Cleaning up temporary files");
            try
            {
                File.Delete(ilPath);
                var resFile = Path.Combine(Path.GetDirectoryName(ilPath), $"{Path.GetFileNameWithoutExtension(ilPath)}.res");
                if (File.Exists(resFile)) File.Delete(resFile);
            }
            catch { /* Ignore cleanup failures */ }
        }

        private void InjectWrappers()
        {
            // Inject a wrapper for each discovered method
            foreach (var kvp in ExportMethods)
            {
                var exportType = kvp.Key;
                var userMethod = kvp.Value;
                Log($"  - Injecting wrapper for {exportType}");

                switch (exportType)
                {
                    case ArmaExport.RVExtension:
                        InjectRVExtensionWrapper(userMethod, "RVExtension");
                        break;
                    case ArmaExport.RVExtensionVersion:
                        InjectRVExtensionVersionWrapper(userMethod, "RVExtensionVersion");
                        break;
                    case ArmaExport.RVExtensionArgs:
                        InjectRVExtensionArgsWrapper(userMethod, "RVExtensionArgs");
                        break;
                        // Add cases for RVExtensionRegisterCallback and RVExtensionContext if you implement them
                }
            }
        }

        // Wrapper for: void __stdcall RVExtension(char *output, int outputSize, const char *function);
        private void InjectRVExtensionWrapper(MethodDefinition userMethod, string wrapperMethodName)
        {
            var wrapperClass = GetOrCreateWrapperClass();
            var voidRef = Module.Import(typeof(void));

            var method = new MethodDefinition(
                wrapperMethodName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                voidRef
            );

            method.Parameters.Add(new ParameterDefinition("output", ParameterAttributes.None, Module.Import(typeof(StringBuilder))));
            method.Parameters.Add(new ParameterDefinition("outputSize", ParameterAttributes.None, Module.Import(typeof(int))));
            method.Parameters.Add(new ParameterDefinition("function", ParameterAttributes.None, Module.Import(typeof(string))) { MarshalInfo = new MarshalInfo(NativeType.LPStr) });

            // Apply __stdcall calling convention
            method.CallingConvention = MethodCallingConvention.StdCall;

            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0)); // output StringBuilder
            il.Append(Instruction.Create(OpCodes.Ldarg_2)); // function string
            il.Append(Instruction.Create(OpCodes.Call, userMethod)); // Call user's static method

            // Append the result string to the output StringBuilder
            var appendMethod = Module.Import(typeof(StringBuilder).GetMethod("Append", new[] { typeof(string) }));
            il.Append(Instruction.Create(OpCodes.Callvirt, appendMethod));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ret));

            wrapperClass.Methods.Add(method);
        }

        // Wrapper for: void __stdcall RVExtensionVersion(char *output, int outputSize);
        private void InjectRVExtensionVersionWrapper(MethodDefinition userMethod, string wrapperMethodName)
        {
            var wrapperClass = GetOrCreateWrapperClass();
            var voidRef = Module.Import(typeof(void));

            var method = new MethodDefinition(
                wrapperMethodName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                voidRef
            );

            method.Parameters.Add(new ParameterDefinition("output", ParameterAttributes.None, Module.Import(typeof(StringBuilder))));
            method.Parameters.Add(new ParameterDefinition("outputSize", ParameterAttributes.None, Module.Import(typeof(int))));
            method.CallingConvention = MethodCallingConvention.StdCall;

            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0)); // output
            il.Append(Instruction.Create(OpCodes.Call, userMethod)); // Call user's static method which must return a string
            var appendMethod = Module.Import(typeof(StringBuilder).GetMethod("Append", new[] { typeof(string) }));
            il.Append(Instruction.Create(OpCodes.Callvirt, appendMethod));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ret));

            wrapperClass.Methods.Add(method);
        }

        // Wrapper for: int __stdcall RVExtensionArgs(char *output, int outputSize, const char *function, const char **argv, int argc);
        private void InjectRVExtensionArgsWrapper(MethodDefinition userMethod, string wrapperMethodName)
        {
            var wrapperClass = GetOrCreateWrapperClass();
            var intRef = Module.Import(typeof(int));

            var method = new MethodDefinition(
                wrapperMethodName,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                intRef // Returns int
            );

            method.Parameters.Add(new ParameterDefinition("output", ParameterAttributes.None, Module.Import(typeof(StringBuilder))));
            method.Parameters.Add(new ParameterDefinition("outputSize", ParameterAttributes.None, Module.Import(typeof(int))));
            method.Parameters.Add(new ParameterDefinition("function", ParameterAttributes.None, Module.Import(typeof(string))) { MarshalInfo = new MarshalInfo(NativeType.LPStr) });

            // For const char** argv, we'll pass it as an IntPtr and marshal it in C#
            var stringArrayType = new PointerType(Module.Import(typeof(string)));
            method.Parameters.Add(new ParameterDefinition("argv", ParameterAttributes.None, stringArrayType) { MarshalInfo = new MarshalInfo(NativeType.LPStr) });

            method.Parameters.Add(new ParameterDefinition("argc", ParameterAttributes.None, Module.Import(typeof(int))));
            method.CallingConvention = MethodCallingConvention.StdCall;

            var il = method.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldarg_0)); // output
            il.Append(Instruction.Create(OpCodes.Ldarg_2)); // function
            il.Append(Instruction.Create(OpCodes.Ldarg_3)); // argv
            il.Append(Instruction.Create(OpCodes.Ldarg_S, (byte)4)); // argc
            il.Append(Instruction.Create(OpCodes.Call, userMethod)); // Call user's method
            il.Append(Instruction.Create(OpCodes.Ret)); // Return the int result

            wrapperClass.Methods.Add(method);
        }

        private TypeDefinition GetOrCreateWrapperClass()
        {
            var wrapperClass = Module.Types.FirstOrDefault(t => t.Namespace == "Arma3.DllExport" && t.Name == "DllExportWrapper");
            if (wrapperClass == null)
            {
                wrapperClass = new TypeDefinition(
                    "Arma3.DllExport",
                    "DllExportWrapper",
                    TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
                );
                Module.Types.Add(wrapperClass);
            }
            return wrapperClass;
        }

        private void RemoveArmaExportRefs()
        {
            foreach (var method in ExportMethods.Values)
            {
                var attribute = method.CustomAttributes.FirstOrDefault(c => c.AttributeType.Name == "ArmaDllExportAttribute");
                if (attribute != null)
                {
                    method.CustomAttributes.Remove(attribute);
                }
            }
            var armaExportRef = Module.AssemblyReferences.FirstOrDefault(a => a.Name == "Arma3.DllExport");
            if (armaExportRef != null)
            {
                Module.AssemblyReferences.Remove(armaExportRef);
            }
        }

        private void IlDasm(string dllPath, string ilPath)
        {
            if (string.IsNullOrEmpty(IldasmPath) || !File.Exists(Path.Combine(IldasmPath, "ildasm.exe")))
                throw new DllExporterException("ildasm.exe not found. Please check your ArmaDllExportSdkPath property.");

            var ildasm = Path.Combine(IldasmPath, "ildasm.exe");
            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "/quoteallnames /unicode /nobar{0} \"/out:{1}\" \"{2}\"",
                Debug ? " /linenum " : "",
                ilPath,
                dllPath
            );
            RunProcess(ildasm, arguments);
        }

        private void IlAsm(string ilPath)
        {
            if (string.IsNullOrEmpty(IlasmPath) || !File.Exists(Path.Combine(IlasmPath, "ilasm.exe")))
                throw new DllExporterException("ilasm.exe not found. Please check your ArmaDllExportFrameworkPath property.");

            var ilasm = Path.Combine(IlasmPath, "ilasm.exe");
            var resFile = Path.Combine(Path.GetDirectoryName(ilPath), $"{Path.GetFileNameWithoutExtension(ilPath)}.res");
            var resArgument = File.Exists(resFile) ? $"\"/res:{resFile}\"" : "";

            var arguments = string.Format(
                CultureInfo.InvariantCulture,
                "/nologo \"/out:{0}\" /DLL {3} {4} {2} \"{1}\"",
                Target,
                ilPath,
                resArgument,
                Debug ? "/debug" : "/optimize",
                Cpu == CpuPlatform.X64 ? "/X64" : "/X86"
            );
            RunProcess(ilasm, arguments);
        }

        private void RunProcess(string fileName, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }))
            {
                process?.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    throw new DllExporterException($"Process '{Path.GetFileName(fileName)}' exited with code {process.ExitCode}.\nOutput:\n{output}\nError:\n{error}");
                }
            }
        }

        // New Logic: Add an export directive for each wrapper method
        private void IlParser(string ilPath)
        {
            var il = File.ReadAllLines(ilPath).ToList();
            var wrapperClassSignature = ".class public auto ansi sealed 'Arma3.DllExport'.'DllExportWrapper'";
            var classStartIndex = il.FindIndex(line => line.Trim().StartsWith(wrapperClassSignature));

            if (classStartIndex == -1)
                throw new DllExporterException("Could not find the injected wrapper class in the IL code.");

            int entryPointCounter = 1;
            var exportsToAdd = new List<string>();

            // Map enum to the exported names and parameter stack sizes (@X) for stdcall name decoration on x86
            var exportDetails = new Dictionary<ArmaExport, (string Name, int StackSize)>
            {
                { ArmaExport.RVExtensionVersion, ("RVExtensionVersion", 8) },
                { ArmaExport.RVExtension, ("RVExtension", 12) },
                { ArmaExport.RVExtensionArgs, ("RVExtensionArgs", 20) },
                { ArmaExport.RVExtensionRegisterCallback, ("RVExtensionRegisterCallback", 4) },
                { ArmaExport.RVExtensionContext, ("RVExtensionContext", 8) },
            };

            foreach (var exportType in ExportMethods.Keys)
            {
                if (!exportDetails.ContainsKey(exportType)) continue;

                var (name, stackSize) = exportDetails[exportType];
                var exportName = Cpu == CpuPlatform.X64 ? name : $"_{name}@{stackSize}";

                exportsToAdd.Add($"    .vtentry {entryPointCounter} : 1");
                exportsToAdd.Add($"    .export [{entryPointCounter}] as {exportName}");
                entryPointCounter++;
            }

            // Find the first method definition inside the wrapper class to insert the exports
            int insertIndex = -1;
            for (int i = classStartIndex; i < il.Count; i++)
            {
                if (il[i].Trim().StartsWith(".method"))
                {
                    insertIndex = i;
                    break;
                }
                if (il[i].Trim() == "}") // End of class
                    break;
            }

            if (insertIndex != -1)
            {
                il.InsertRange(insertIndex, exportsToAdd);
            }
            else
            {
                throw new DllExporterException("Could not find a method in the wrapper class to insert export directives.");
            }

            File.WriteAllLines(ilPath, il);
        }
    }
}