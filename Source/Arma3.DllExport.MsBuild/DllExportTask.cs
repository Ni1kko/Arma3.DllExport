using System;
using System.IO;
using System.Reflection;
using System.Security.Permissions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Arma3.DllExport.MsBuild
{
    [LoadInSeparateAppDomain]
    [PermissionSet(SecurityAction.InheritanceDemand, Name = "FullTrust")]
    public class ArmaDllExportTask : AppDomainIsolatedTask
    {
        /// <summary>
        /// This static constructor runs once when the class is first used.
        /// It subscribes to the AssemblyResolve event, which fires when the runtime
        /// fails to find a required DLL.
        /// </summary>
        static ArmaDllExportTask()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveCecil;
        }

        /// <summary>
        /// This method is called when the runtime can't find an assembly.
        /// It checks if the missing assembly is Mono.Cecil and, if so, loads it
        /// from the same directory as our task's DLL.
        /// </summary>
        private static Assembly ResolveCecil(object sender, ResolveEventArgs args)
        {
            // We only care about Mono.Cecil
            if (!args.Name.StartsWith("Mono.Cecil,"))
            {
                return null;
            }

            // Get the directory where our task DLL (Arma3.DllExport.MsBuild.dll) is located.
            var assemblyPath = typeof(ArmaDllExportTask).Assembly.Location;
            var assemblyDir = Path.GetDirectoryName(assemblyPath);
            var cecilPath = Path.Combine(assemblyDir, "Mono.Cecil.dll");

            // Load and return the assembly from that path.
            return File.Exists(cecilPath) ? Assembly.LoadFrom(cecilPath) : null;
        }

        [Required]
        public string FileName { get; set; }

        [Required]
        public string FrameworkPath { get; set; }

        [Required]
        public string SdkPath { get; set; }

        public bool KeepIl { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override bool Execute()
        {
            DllExporter.IldasmPath = SdkPath;
            DllExporter.IlasmPath = FrameworkPath;
            DllExporter dll;
            try
            {
                dll = new DllExporter(FileName);
            }
            catch (DllExporterException ex)
            {
                Log.LogError("There was a problem initialising the exporter:");
                Log.LogErrorFromException(ex);
                return false;
            }
            if (!dll.FoundMethod)
            {
                Log.LogMessage("No export method was found - did you forget the ArmaDllExport attribute?");
                return true;
            }
            dll.KeepIl = KeepIl;
            dll.Log = s => Log.LogMessage(s);
            try
            {
                dll.Export();
            }
            catch (DllExporterException ex)
            {
                Log.LogError("There was a problem exporting:");
                Log.LogErrorFromException(ex);
                return false;
            }
            return true;
        }
    }
}