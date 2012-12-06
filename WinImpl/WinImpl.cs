﻿using System;
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
    public class WinImpl : IProvisioner
    {
        protected const string SysRegPostfix = @"\System32\config";
        protected static readonly List<string> VHDExtensions = new List<string>(new []{"vhd", "vhdx", "avhd", "avhdx"});

        public string[] MountVHD(out bool Status, string VHD, string ProxyVM = null, string AlternateInterface = null)
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
            
            

            Status = true;
            return GetMountPoints(VHD);
        }

        public bool UnmountVHD(string VHD, string ProxyVM = null, string AlternateInterface = null)
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

        public bool WriteFile(byte[] Data, string Location, string ProxyVM = null, string AlternateInterface = null)
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

        public byte[] ReadFile(out bool Status, string Location, string ProxyVM = null, string AlternateInterface = null)
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

        public bool WriteUserRegistry(string Root, string Username, string DataPath, object Data, string DataType, string ProxyVM = null, string AlternateInterface = null)
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
                    bool mountStatus;
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return false;
                }
                userFileRoot = arr.Where(s => LocateUserRoot(s + @"\") != null).FirstOrDefault();
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
            for (int i = 0; i < parts.Length - 1; i++)
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
            return true;
        }

        public bool WriteMachineRegistry(string Root, string DataPath, object Data, string DataType, string ProxyVM = null, string AlternateInterface = null)
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
                    bool mountStatus;
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return false;
                }
                winRoot = arr.Where(s => DetectWindows(s + @"\") != null).FirstOrDefault();
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
            return true;
        }

        public object ReadUserRegistry(out bool Status, string Root, string Username, string DataPath, string ProxyVM = null, string AlternateInterface = null)
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
                    bool mountStatus;
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return null;
                }
                userFileRoot = arr.Where(s => LocateUserRoot(s + @"\") != null).FirstOrDefault();
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
            for (int i = 0; i < parts.Length - 1; i++)
            {
                location = location.OpenSubKey(parts[i]);
                if (location == null)
                    return null;
            }
            var retval = location.GetValue(parts[parts.Length - 1]);
            Status = true;
            RegExtra.UnloadHive(RegistryHive.LocalMachine, hiveFile);
            return retval;
        }

        public object ReadMachineRegistry(out bool Status, string Root, string DataPath, string ProxyVM = null, string AlternateInterface = null)
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

            string winRoot = null;
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
                    bool mountStatus;
                    arr = MountVHD(out mountStatus, Root);

                    if (mountStatus == false)
                        return null;
                }
                winRoot = arr.Where(s => DetectWindows(s + @"\") != null).FirstOrDefault();
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
            for (int i = partIndex + 1; i < parts.Length - 1; i++)
            {
                location = location.OpenSubKey(parts[i]);
                if (location == null)
                    return null;
            }
            var retval = location.GetValue(parts[parts.Length - 1]);
            Status = true;
            RegExtra.UnloadHive(RegistryHive.LocalMachine, hiveFile);
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
            return ret.ToArray();
        }

    }

    internal class RegExtra
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct LUID
        {
            public int LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public LUID Luid;
            public uint Attributes;
            public int PrivilegeCount;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Auto)]
        public static extern bool OpenProcessToken(int ProcessHandle, uint DesiredAccess,
                                                  ref int tokenhandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern int GetCurrentProcess();

        [DllImport("advapi32.dll", CharSet = CharSet.Auto)]
        public static extern bool LookupPrivilegeValue(string lpsystemname, string lpname,
                                                      [MarshalAs(UnmanagedType.Struct)] ref LUID lpLuid);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto)]
        public static extern bool AdjustTokenPrivileges(int tokenhandle, bool disableprivs,
                                                       [MarshalAs(UnmanagedType.Struct)] ref TOKEN_PRIVILEGES Newstate,
                                                       int bufferlength,
                                                       int PreivousState, int Returnlength);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int RegLoadKey(uint hKey, string lpSubKey, string lpFile);

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int RegUnLoadKey(uint hKey, string lpSubKey);

        public const uint TOKEN_ADJUST_PRIVILEGES = 0x00000020;
        public const uint TOKEN_QUERY = 0x00000008;
        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        public const string SE_RESTORE_NAME = "SeRestorePrivilege";
        public const string SE_BACKUP_NAME = "SeBackupPrivilege";
        //        public string shortname;
        //        private bool unloaded = false;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Base">The existing hive root to load as a subkey of.</param>
        /// <param name="NewHive">The registry hive file to mount/load.</param>
        /// <returns>String containing the subkey from the Base where NewHive was mounted.  Null if unsuccessful.</returns>
        public static string LoadHive(RegistryHive Base, string NewHive)
        {
            try
            {
                int token = 0;
                LUID RestLUID = new LUID();
                LUID BackLUID = new LUID();
                TOKEN_PRIVILEGES RestPriv = new TOKEN_PRIVILEGES { Attributes = SE_PRIVILEGE_ENABLED, PrivilegeCount = 1 };
                TOKEN_PRIVILEGES BackPriv = new TOKEN_PRIVILEGES { Attributes = SE_PRIVILEGE_ENABLED, PrivilegeCount = 1 };

                OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref token);
                LookupPrivilegeValue(null, SE_RESTORE_NAME, ref RestLUID);
                LookupPrivilegeValue(null, SE_BACKUP_NAME, ref BackLUID);
                RestPriv.Luid = RestLUID;
                BackPriv.Luid = BackLUID;
                AdjustTokenPrivileges(token, false, ref RestPriv, 0, 0, 0);
                AdjustTokenPrivileges(token, false, ref BackPriv, 0, 0, 0);

                string reference = (Path.GetFileNameWithoutExtension(NewHive) + DateTime.Now.Ticks + '-' + new Random().Next());
                return RegLoadKey((uint)Base, reference, NewHive) == 0 ? reference : null;
            }
            catch (Exception)
            { return null; }
        }

        public static bool UnloadHive(RegistryHive Base, string Reference)
        {
            try
            {
                int token = 0;
                LUID RestLUID = new LUID();
                LUID BackLUID = new LUID();
                TOKEN_PRIVILEGES RestPriv = new TOKEN_PRIVILEGES { Attributes = SE_PRIVILEGE_ENABLED, PrivilegeCount = 1 };
                TOKEN_PRIVILEGES BackPriv = new TOKEN_PRIVILEGES { Attributes = SE_PRIVILEGE_ENABLED, PrivilegeCount = 1 };

                OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref token);
                LookupPrivilegeValue(null, SE_RESTORE_NAME, ref RestLUID);
                LookupPrivilegeValue(null, SE_BACKUP_NAME, ref BackLUID);
                RestPriv.Luid = RestLUID;
                BackPriv.Luid = BackLUID;
                AdjustTokenPrivileges(token, false, ref RestPriv, 0, 0, 0);
                AdjustTokenPrivileges(token, false, ref BackPriv, 0, 0, 0);

                return RegUnLoadKey((uint)Base, Reference) == 0;
            }
            catch (Exception)
            { return false; }
        }

    }

}
