using System;

namespace Arma3.DllExport
{
    /// <summary>
    /// Defines the type of Arma 3 extension function to export.
    /// </summary>
    public enum ArmaExport
    {
        // [ArmaDllExport(ArmaExport.RVExtension)]
        RVExtension,
        // [ArmaDllExport(ArmaExport.RVExtensionVersion)]
        RVExtensionVersion,
        // [ArmaDllExport(ArmaExport.RVExtensionArgs)]
        RVExtensionArgs,
        // [ArmaDllExport(ArmaExport.RVExtensionRegisterCallback)]
        RVExtensionRegisterCallback,
        // [ArmaDllExport(ArmaExport.RVExtensionContext)]
        RVExtensionContext
    }

    /// <summary>
    /// Attribute to mark a method for export to an Arma 3 extension DLL.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ArmaDllExportAttribute : Attribute
    {
        public ArmaExport Export { get; }

        public ArmaDllExportAttribute(ArmaExport export)
        {
            Export = export;
        }
    }
}