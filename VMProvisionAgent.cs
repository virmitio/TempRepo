using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Text;

namespace VMProvisioningAgent
{
    public interface IProvisioner
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Status"></param>
        /// <param name="VHD"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns>An array of location references that may be used by other commands in the same interface implementation.</returns>
        string[] MountVHD(out bool Status, string VHD, string ProxyVM = null, string AlternateInterface = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="VHD"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        bool UnmountVHD(string VHD, string ProxyVM = null, string AlternateInterface = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Data"></param>
        /// <param name="Location"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        bool WriteFile(byte[] Data, string Location, string ProxyVM = null, string AlternateInterface = null);
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Status"></param>
        /// <param name="Location"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        byte[] ReadFile(out bool Status, string Location, string ProxyVM = null, string AlternateInterface = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Root">Location to find registry hives.  This may be a drive letter or a path to a VHD file.</param>
        /// <param name="Username"></param>
        /// <param name="DataPath"></param>
        /// <param name="Data"></param>
        /// <param name="DataType"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        bool WriteUserRegistry(string Root, string Username, string DataPath, object Data, string DataType = "string", string ProxyVM = null, string AlternateInterface = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Root">Location to find registry hives.  This may be a drive letter or a path to a VHD file.</param>
        /// <param name="DataPath"></param>
        /// <param name="Data"></param>
        /// <param name="DataType"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        bool WriteMachineRegistry(string Root, string DataPath, object Data, string DataType = "string", string ProxyVM = null, string AlternateInterface = null);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Status"></param>
        /// <param name="Root">Location to find registry hives.  This may be a drive letter or a path to a VHD file.</param>
        /// <param name="Username"></param>
        /// <param name="DataPath"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        object ReadUserRegistry(out bool Status, string Root, string Username, string DataPath, string ProxyVM = null, string AlternateInterface = null);
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Status"></param>
        /// <param name="Root">Location to find registry hives.  This may be a drive letter or a path to a VHD file.</param>
        /// <param name="DataPath"></param>
        /// <param name="ProxyVM"></param>
        /// <param name="AlternateInterface"></param>
        /// <returns></returns>
        object ReadMachineRegistry(out bool Status, string Root, string DataPath, string ProxyVM = null, string AlternateInterface = null);
    }

    public static class PluginLoader
    {
        private class typedef
        {
            public typedef(Type type, string file)
            {
                Type = type;
                FileIndex = file;
            }
            public readonly Type Type;
            public readonly string FileIndex;
        }

        private static bool AlreadyScanned = false;
        private static List<string> files = new List<string>();
        private static Dictionary<string, typedef> types = new Dictionary<string, typedef>();

        public static Type FindType(string Name)
        {
            if (Name == null)
                return null;
            if (types.ContainsKey(Name))
                return types[Name].Type;
            return null;
        }

        public static void ScanForPlugins(bool Force = false, string path = null)
        {
            if (AlreadyScanned && !Force)
                return;
            files.Clear();
            types.Clear();

            string PlugPath = path ??
                           Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Plugins");
            string[] Plugs = Directory.GetFiles(PlugPath, "*.dll");
            foreach (string PlugFile in Plugs)
            {
                try
                {
                    var plug = Assembly.LoadFrom(PlugFile);
                    var PlugTypes = plug.GetTypes();
                    foreach (Type plugType in PlugTypes)
                    {
                        
                        if (!(plugType.FindInterfaces((t, o) => t.GetMethods().SequenceEqual(((Type)o).GetMethods()), typeof(IProvisioner))).Any()) continue;
                        if (!files.Contains(PlugFile))
                            files.Add(PlugFile);
                        if (!types.ContainsKey(plugType.Name))
                            types.Add(plugType.Name, new typedef(plugType, PlugFile));
                    }
                }
                catch (Exception e)
                {
                    //TODO: 
                    //Output "Failed to load plugin file: <Path.GetFileName(PlugFile)>"
                    throw e;
                }
            }
            AlreadyScanned = true;
        }
    }
}
