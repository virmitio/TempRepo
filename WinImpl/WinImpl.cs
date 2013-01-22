using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Management;
using Microsoft.Win32;
using VMProvisioningAgent;

namespace WinImpl
{
    public class WinImpl : IVMStateEditor
    {
        protected const string SysRegPostfix = @"\System32\config";
        protected static readonly List<string> VHDExtensions = new List<string>(new []{"vhd", "vhdx", "avhd", "avhdx"});

        public string[] MountVHD(out bool Status, string VHD, string ProxyVM = null, string AlternateInterface = null)
        {
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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
                var tmp = GetMountPoints(VHD);
                return (Status = tmp.Any()) ? tmp : null;
            }

            Status = true;
            return GetMountPoints(VHD);
        }

        public bool UnmountVHD(string VHD, string ProxyVM = null, string AlternateInterface = null)
        {
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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
            if (!GetMountPoints(VHD).Any()) // not mounted, skip to success
                return true;

            ManagementObject SvcObj = Utility.GetServiceObject(Utility.GetScope(), Utility.ServiceNames.ImageManagement);
            ManagementObject result = (ManagementObject)SvcObj.InvokeMethod("Unmount", new object[] { VHD });
            return ((int) result["ReturnValue"] == 0);
        }

        public bool WriteFile(byte[] Data, string Location, string ProxyVM = null, string AlternateInterface = null)
        {
            if (Location == null || Location.Equals(String.Empty))
                return false;

            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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

        public byte[] ReadFile(out bool Status, string Location, string ProxyVM = null, string AlternateInterface = null)
        {
            // Until proven otherwise, we assume the status is 'False'
            Status = false;
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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

        public bool WriteUserRegistry(string Root, string Username, string DataPath, object Data, string DataType, string ProxyVM = null, string AlternateInterface = null)
        {
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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
                    return Alt.ReadUserRegistry(Root, Username, DataPath, Data, DataType, ProxyVM);
                }
                return false;
            }

            if (ProxyVM != null)
            {
                //TODO: Issue remote registry command to the VM
                throw new NotImplementedException();
            }

            string userFileRoot = null;
            bool mountStatus = false;
            if (Path.HasExtension(Root) &&
                VHDExtensions.Contains(Path.GetExtension(Root), StringComparer.InvariantCultureIgnoreCase))
            {
                //This is a VHD file, mount it, and check each partition on it for a windows install.
                // Use the first win install we find.
                // If we don't find one, return Null with Status set to 'False'

                //Is this drive already mounted?
                var arr = GetMountPoints(Root) ?? new string[0];
                if (arr.Length == 0)
                {
                    //Nope, not mounted yet.  Do so now.
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return false;
                }
                userFileRoot = arr.FirstOrDefault(s => LocateUserRoot(s + @"\") != null);
            }
            else
            {
                // Not a VHD file.  Find Windows on this partition.
                userFileRoot = LocateUserRoot(Root);
            }
            if (userFileRoot == null) // Can't get there from here.
                return false;

            // Ok, we have a root Users directory.  Check for our user and load if possible.
            string userPath = Path.Combine(userFileRoot, Username);
            if (!Directory.Exists(userPath))
                throw new ArgumentException("Username not found.  Invalid user path: " + userPath);

            string hiveFile = Path.Combine(userPath, @"NTUSER.DAT");
            if (!File.Exists(hiveFile))
                throw new ArgumentException("User data not found.  Invalid file path: " + hiveFile);

            string regRoot = RegExtra.LoadHive(RegistryHive.LocalMachine, hiveFile);
            if (regRoot == null)
                return false;
            var location = Registry.LocalMachine.OpenSubKey(regRoot);
            if (location == null)
                return false;
            var parts = DataPath.Split('\\');

            // check for pathing...
            int startIndex = 0;
            if (parts[0].Equals("HKCU", StringComparison.InvariantCultureIgnoreCase) ||
                parts[0].Equals("HKEY_CURRENT_USER", StringComparison.InvariantCultureIgnoreCase))
            {
                startIndex = 1;
            }

            for (int i = startIndex; i < parts.Length - 1; i++)
            {
                location = location.CreateSubKey(parts[i]);
                if (location == null)
                    return false;
            }
            RegistryValueKind type;
            if (Enum.TryParse(DataType, out type))
                location.SetValue(parts[parts.Length - 1], Data, type);
            else
                location.SetValue(parts[parts.Length - 1], Data);
            RegExtra.UnloadHive(RegistryHive.LocalMachine, hiveFile);
            if (mountStatus)
                UnmountVHD(Root);
            return true;
        }

        public bool WriteMachineRegistry(string Root, string DataPath, object Data, string DataType, string ProxyVM = null, string AlternateInterface = null)
        {
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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
                    return Alt.WriteMachineRegistry(Root, DataPath, Data, DataType, ProxyVM);
                }
                return false;
            }

            if (ProxyVM != null)
            {
                //TODO: Issue remote registry command to the VM
                throw new NotImplementedException();
            }

            string winRoot = null;
            bool mountStatus = false;
            if (Path.HasExtension(Root) &&
                VHDExtensions.Contains(Path.GetExtension(Root), StringComparer.InvariantCultureIgnoreCase))
            {
                //This is a VHD file, mount it, and check each partition on it for a windows install.
                // Use the first win install we find.
                // If we don't find one, return Null with Status set to 'False'

                //Is this drive already mounted?
                var arr = GetMountPoints(Root) ?? new string[0];
                if (arr.Length == 0)
                {
                    //Nope, not mounted yet.  Do so now.
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return false;
                }
                winRoot = arr.FirstOrDefault(s => DetectWindows(s + @"\") != null);
            }
            else
            {
                // Not a VHD file.  Find Windows on this partition.
                winRoot = DetectWindows(Root);
            }
            if (winRoot == null) // Can't get there from here.
                return false;

            // Ok, we have a Windows registry (or appear to, at any rate).  What hive do we need to load?
            var parts = DataPath.Split('\\');
            int partIndex = 0;
            bool partFound = false;
            while (!partFound && partIndex < parts.Length)
            {
                if (parts[partIndex].Equals("SOFTWARE", StringComparison.InvariantCultureIgnoreCase) ||
                    parts[partIndex].Equals("SYSTEM", StringComparison.InvariantCultureIgnoreCase))
                    partFound = true;
                else
                    partIndex++;
            }
            if (!partFound)
                throw new ArgumentException("DataPath must refer to SOFTWARE or SYSTEM roots.");

            string hiveFile = Path.Combine(winRoot + SysRegPostfix, parts[partIndex]);

            string newRoot = RegExtra.LoadHive(RegistryHive.LocalMachine, hiveFile);
            if (newRoot == null)
                return false;
            var location = Registry.LocalMachine.OpenSubKey(newRoot);
            if (location == null)
                return false;

            // try to be proactive about CurrentControlSet to avoid needless extra calls.
            if (parts[partIndex + 1].Equals("CurrentControlSet", StringComparison.InvariantCultureIgnoreCase))
            {
                var tmpLoc = location.OpenSubKey("Select");
                if (tmpLoc != null)
                {
                    uint num = (uint)tmpLoc.GetValue("Current");
                    parts[partIndex + 1] = String.Format("ControlSet{0:000}", num);
                }
            }

            for (int i = partIndex + 1; i < parts.Length - 1; i++)
            {
                location = location.CreateSubKey(parts[i]);
                if (location == null)
                    return false;
            }

            RegistryValueKind type;
            if (Enum.TryParse(DataType, out type))
                location.SetValue(parts[parts.Length - 1], Data, type);
            else
                location.SetValue(parts[parts.Length - 1], Data);
            RegExtra.UnloadHive(RegistryHive.LocalMachine, hiveFile);
            if (mountStatus)
                UnmountVHD(Root);
            return true;
        }

        public object ReadUserRegistry(out bool Status, string Root, string Username, string DataPath, string ProxyVM = null, string AlternateInterface = null)
        {
            // Until proven otherwise, we assume the status is 'False'
            Status = false;
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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
                    return Alt.ReadUserRegistry(out Status, Root, Username, DataPath, ProxyVM);
                }
                return null;
            }

            if (ProxyVM != null)
            {
                //TODO: Issue remote registry command to the VM
                throw new NotImplementedException();
            }

            string userFileRoot = null;
            bool mountStatus = false;
            if (Path.HasExtension(Root) &&
                VHDExtensions.Contains(Path.GetExtension(Root), StringComparer.InvariantCultureIgnoreCase))
            {
                //This is a VHD file, mount it, and check each partition on it for a windows install.
                // Use the first win install we find.
                // If we don't find one, return Null with Status set to 'False'

                //Is this drive already mounted?
                var arr = GetMountPoints(Root) ?? new string[0];
                if (arr.Length == 0)
                {
                    //Nope, not mounted yet.  Do so now.
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return null;
                }
                userFileRoot = arr.FirstOrDefault(s => LocateUserRoot(s + @"\") != null);
            }
            else
            {
                // Not a VHD file.  Find Windows on this partition.
                userFileRoot = LocateUserRoot(Root);
            }
            if (userFileRoot == null) // Can't get there from here.
                return null;

            // Ok, we have a root Users directory.  Check for our user and load if possible.
            string userPath = Path.Combine(userFileRoot, Username);
            if (!Directory.Exists(userPath))
                throw new ArgumentException("Username not found.  Invalid user path: "+userPath);

            string hiveFile = Path.Combine(userPath, @"NTUSER.DAT");
            if (!File.Exists(hiveFile))
                throw new ArgumentException("User data not found.  Invalid file path: " + hiveFile);

            string regRoot = RegExtra.LoadHive(RegistryHive.LocalMachine, hiveFile);
            if (regRoot == null)
                return null;
            var location = Registry.LocalMachine.OpenSubKey(regRoot);
            if (location == null)
                return null;
            var parts = DataPath.Split('\\');
            
            // check for pathing...
            int startIndex = 0;
            if (parts[0].Equals("HKCU", StringComparison.InvariantCultureIgnoreCase) ||
                parts[0].Equals("HKEY_CURRENT_USER", StringComparison.InvariantCultureIgnoreCase))
            {
                startIndex = 1;
            }

            for (int i = startIndex; i < parts.Length - 1; i++)
            {
                location = location.OpenSubKey(parts[i]);
                if (location == null)
                    return null;
            }
            var retval = location.GetValue(parts[parts.Length - 1]);
            Status = true;
            RegExtra.UnloadHive(RegistryHive.LocalMachine, hiveFile);
            if (mountStatus)
                UnmountVHD(Root);
            return retval;
        }

        public object ReadMachineRegistry(out bool Status, string Root, string DataPath, string ProxyVM = null, string AlternateInterface = null)
        {
            // Until proven otherwise, we assume the status is 'False'
            Status = false;
            if (AlternateInterface != null && AlternateInterface != this.GetType().Name)
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

            string winRoot = null;
            bool mountStatus = false;
            if (Path.HasExtension(Root) &&
                VHDExtensions.Contains(Path.GetExtension(Root), StringComparer.InvariantCultureIgnoreCase))
            {
                //This is a VHD file, mount it, and check each partition on it for a windows install.
                // Use the first win install we find.
                // If we don't find one, return Null with Status set to 'False'

                //Is this drive already mounted?
                var arr =MountVHD(out mountStatus, Root);

                if (mountStatus == false)
                    return null;

                winRoot = arr.FirstOrDefault(s => DetectWindows(s + @"\") != null);
            }
            else
            {
                // Not a VHD file.  Find Windows on this partition.
                winRoot = DetectWindows(Root);
            }
            if (winRoot == null) // Can't get there from here.
                return null;

            // Ok, we have a Windows registry (or appear to, at any rate).  What hive do we need to load?
            var parts = DataPath.Split('\\');
            int partIndex = 0;
            bool partFound = false;
            while (!partFound && partIndex < parts.Length)
            {
                if (parts[partIndex].Equals("SOFTWARE", StringComparison.InvariantCultureIgnoreCase) ||
                    parts[partIndex].Equals("SYSTEM", StringComparison.InvariantCultureIgnoreCase))
                    partFound = true;
                else
                    partIndex++;
            }
            if (!partFound)
                throw new ArgumentException("DataPath must refer to SOFTWARE or SYSTEM roots.");

            string hiveFile = Path.Combine(winRoot + SysRegPostfix, parts[partIndex]);

            string newRoot = RegExtra.LoadHive(RegistryHive.LocalMachine, hiveFile);
            if (newRoot == null)
                return null;
            var location = Registry.LocalMachine.OpenSubKey(newRoot);
            if (location == null)
                return null;
            
            // try to be proactive about CurrentControlSet to avoid needless extra calls.
            if (parts[partIndex + 1].Equals("CurrentControlSet", StringComparison.InvariantCultureIgnoreCase))
            {
                var tmpLoc = location.OpenSubKey("Select");
                if (tmpLoc != null)
                {
                    uint num = (uint)tmpLoc.GetValue("Current");
                    parts[partIndex + 1] = String.Format("ControlSet{0:000}", num);
                }
            }

            for (int i = partIndex + 1; i < parts.Length - 1; i++)
            {
                location = location.OpenSubKey(parts[i]);
                if (location == null)
                    return null;
            }
            var retval = location.GetValue(parts[parts.Length - 1]);
            Status = true;
            RegExtra.UnloadHive(RegistryHive.LocalMachine, hiveFile);
            if (mountStatus)
                UnmountVHD(Root);
            return retval;
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
        
        public string LocateUserRoot(string WindowsRoot)
        {
            bool Status;
            string init = ReadMachineRegistry(out Status, WindowsRoot,
                                              @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders\Common Desktop") as string;
            if (init == null)
                return null;
            string initPath = init.Split('\\')
                                  .Take(init.Split('\\').Length - 2)
                                  .Skip(1)
                                  .Aggregate("", (current, item) =>
                                                    current + item);
            string testPath = Path.Combine(Path.GetPathRoot(WindowsRoot), initPath);
            if (Directory.Exists(testPath))
                return testPath;
            foreach (var drive in GetMountPoints(Utility.PathToVHD(WindowsRoot)))
            {
                testPath = Path.Combine(drive + @"\", initPath);
                if (Directory.Exists(testPath))
                    return testPath;
            }
            return null;
        }

        public static string[] GetMountPoints(string VHD)
        {
            if (VHD == null)
                return null;
            List<string> ret = new List<string>();
            var image = new ManagementObjectSearcher(Utility.GetScope(),
                                                     new SelectQuery(Utility.DiskStrings.MountedStorage)).Get()
                                                                                                         .Cast<ManagementObject>()
                                                                                                         .FirstOrDefault(Obj => (Obj["Name"].ToString()
                                                                                                                                            .Equals(VHD, StringComparison.InvariantCultureIgnoreCase)));
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
            return ret.ToArray();
        }

    }

}
