using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Management;
using VMProvisioningAgent;

namespace WinImpl
{
    public class WinImpl : IProvisioner
    {
        public string[] MountVHD(out bool Status, string VHD, string ProxyVM, string AlternateInterface)
        {
            if (AlternateInterface != null)
            {
                Type Plug = PluginLoader.FindType(AlternateInterface);
                if (Plug == null)
                {
                    PluginLoader.ScanForPlugins();
                    Plug = PluginLoader.FindType(AlternateInterface);
                }
                if (Plug != null)
                {
                    dynamic Alt = Activator.CreateInstance(Plug);
                    return Alt.MountVHD(out Status, VHD, ProxyVM);
                }
                Status = false;
                return null;
            }

            if (ProxyVM != null)
            {
                //TODO: Mount to VM, then report location (if possible?)
                throw new NotImplementedException();
            }

            ManagementObject SvcObj = Utility.GetServiceObject(Utility.GetScope(), Utility.ServiceNames.ImageManagement);
            int result = Utility.WaitForJob((ManagementObject)SvcObj.InvokeMethod("Mount", new object[] { VHD }));
            if (result != 0)
            {
                Status = false;
                return null;
            }
            
            List<string> ret = new List<string>();
            var image = new ManagementObjectSearcher(Utility.GetScope(), new SelectQuery(Utility.DiskStrings.MountedStorage)).Get()
                            .Cast<ManagementObject>()
                            .Where(Obj => (Obj["Name"].ToString().Equals(VHD, StringComparison.InvariantCultureIgnoreCase)))
                            .FirstOrDefault();
            var baseScope = new ManagementScope(@"root\cimv2");
            baseScope.Connect();
            var disk = new ManagementObjectSearcher(baseScope,
                                                    new SelectQuery(Utility.DiskStrings.DiskDrive,
                                                                    "SCSILogicalUnit=" + image["Lun"] +
                                                                    " and SCSIPort=" + image["PortNumber"] +
                                                                    " and SCSITargetID=" + image["TargetID"]))
                .Get().Cast<ManagementObject>().FirstOrDefault();
            var parts = disk.GetRelated(Utility.DiskStrings.DiskPartition);
            foreach (var drives in from ManagementObject part in parts select part.GetRelated(Utility.DiskStrings.LogicalDisk))
            {
                ret.AddRange(from ManagementObject drive in drives select drive["DeviceID"].ToString());
            }

            Status = true;
            return ret.ToArray();
        }

        public bool UnmountVHD(string VHD, string ProxyVM, string AlternateInterface)
        {
            if (AlternateInterface != null)
            {
                Type Plug = PluginLoader.FindType(AlternateInterface);
                if (Plug == null)
                {
                    PluginLoader.ScanForPlugins();
                    Plug = PluginLoader.FindType(AlternateInterface);
                }
                if (Plug != null)
                {
                    dynamic Alt = Activator.CreateInstance(Plug);
                    return Alt.UnmountVHD(VHD, ProxyVM);
                }
                return false;
            }

            if (ProxyVM != null)
            {
                //TODO: Unmount to VM, then report location (if possible?)
                throw new NotImplementedException();
            }

            ManagementObject SvcObj = Utility.GetServiceObject(Utility.GetScope(), Utility.ServiceNames.ImageManagement);
            ManagementObject result = (ManagementObject)SvcObj.InvokeMethod("Unmount", new object[] { VHD });
            return ((int) result["ReturnValue"] == 0);
        }

        public bool WriteFile(byte[] Data, string Location, string ProxyVM, string AlternateInterface)
        {
            if (Location == null || Location.Equals(String.Empty))
                return false;

            if (AlternateInterface != null)
            {
                Type Plug = PluginLoader.FindType(AlternateInterface);
                if (Plug == null)
                {
                    PluginLoader.ScanForPlugins();
                    Plug = PluginLoader.FindType(AlternateInterface);
                }
                if (Plug != null)
                {
                    dynamic Alt = Activator.CreateInstance(Plug);
                    return Alt.WriteFile(Data, Location, ProxyVM);
                }
                return false;
            }

            if (ProxyVM != null)
            {
                //TODO: Issue remote write command to the VM
                throw new NotImplementedException();
            }

            if (!Utility.PathIsFromVHD(Location))
                throw new InvalidOperationException("This method may only target mounted VHDs.");

            try
            {
                var file = File.Open(Location, FileMode.OpenOrCreate, FileAccess.Write);
                file.Write(Data, 0, Data.Length);
                file.SetLength(Data.Length);
                return true;
            }
            catch(Exception)
            {
                return false;
            }
        }

        public byte[] ReadFile(out bool Status, string Location, string ProxyVM, string AlternateInterface)
        {
            // Until proven otherwise, we assume the status is 'False'
            Status = false;
            if (AlternateInterface != null)
            {
                Type Plug = PluginLoader.FindType(AlternateInterface);
                if (Plug == null)
                {
                    PluginLoader.ScanForPlugins();
                    Plug = PluginLoader.FindType(AlternateInterface);
                }
                if (Plug != null)
                {
                    dynamic Alt = Activator.CreateInstance(Plug);
                    return Alt.ReadFile(out Status, Location, ProxyVM);
                }
                return null;
            }

            if (ProxyVM != null)
            {
                //TODO: Issue remote read command to the VM
                throw new NotImplementedException();
            }

            try
            {
                var file = File.OpenRead(Location);
                var data = new byte[file.Length];
                file.Read(data, 0, data.Length);
                Status = true;
                return data;
            }
            catch (Exception)
            {
                return null;
            }
        }

        const string SysRegPostfix = @"\System32\config";

        public bool WriteUserRegistry(string Root, string Username, string DataPath, object Data, string DataType, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public bool WriteMachineRegistry(string Root, string DataPath, object Data, string DataType, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public object ReadUserRegistry(out bool Status, string Root, string Username, string DataPath, string ProxyVM, string AlternateInterface)
        {
            throw new NotImplementedException();
        }

        public object ReadMachineRegistry(out bool Status, string Root, string DataPath, string ProxyVM, string AlternateInterface)
        {
            // Until proven otherwise, we assume the status is 'False'
            Status = false;
            if (AlternateInterface != null)
            {
                Type Plug = PluginLoader.FindType(AlternateInterface);
                if (Plug == null)
                {
                    PluginLoader.ScanForPlugins();
                    Plug = PluginLoader.FindType(AlternateInterface);
                }
                if (Plug != null)
                {
                    dynamic Alt = Activator.CreateInstance(Plug);
                    return Alt.ReadMachineRegistry(out Status, Root, DataPath, ProxyVM);
                }
                return null;
            }

            if (ProxyVM != null)
            {
                //TODO: Issue remote registry command to the VM
                throw new NotImplementedException();
            }

            if (Path.HasExtension(Root) &&
                Path.GetExtension(Root))
            {
                
            }

            string winRoot = DetectWindows(Root);
            if (winRoot == null)
                return null;


            throw new NotImplementedException();
        }

        /// <summary>
        /// Attempts to locate an installation of Windows on the drive specified by Location
        /// </summary>
        /// <param name="Location">An arbitrary path on the drive to search.</param>
        /// <returns>The root folder of the Windows installation if one is found.  Null otherwise.</returns>
        public static string DetectWindows(string Location)
        {
            try
            {
                string path = Path.GetPathRoot(Location);
                var dirs = Directory.GetDirectories(path);
                return dirs.FirstOrDefault(dir =>
                    File.Exists(dir + SysRegPostfix + @"\SYSTEM") && 
                    File.Exists(dir + SysRegPostfix + @"\SOFTWARE"));
            }
            catch (Exception)
            {
                return null;
            }
        }


        public static string LocateUserRoot(string WindowsRoot)
        {
            
        }
    }
}
