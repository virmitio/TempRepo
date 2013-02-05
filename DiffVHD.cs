using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DiscUtils;

namespace VMProvisioningAgent
{
    class DiffVHD
    {
        public enum DiskType
        {
            VHD,
            VHDX
        }

        protected const string VHDVarient = "dynamic"; // choose from "fixed" or "dynamic"

        protected const string RootFiles = "FILES";
        protected const string RootSystemRegistry = @"REGISTRY\SYSTEM";
        protected const string RootUserRegistry = @"REGISTRY\USERS";

        protected static readonly string[] ExcludeFiles = new string[] { @"\PAGEFILE.SYS", @"\HIBERFIL.SYS", @"\SYSTEM VOLUME INFORMATION", @"\WINDOWS\SYSTEM32\CONFIG" };
        protected static readonly string[] SystemRegistryFiles = new string[]
            {
                @"\WINDOWS\SYSTEM32\CONFIG\BCD-TEMPLATE",
                @"\WINDOWS\SYSTEM32\CONFIG\COMPONENTS",
                @"\WINDOWS\SYSTEM32\CONFIG\DEFAULT",
                @"\WINDOWS\SYSTEM32\CONFIG\DRIVERS",
                @"\WINDOWS\SYSTEM32\CONFIG\FP",
                @"\WINDOWS\SYSTEM32\CONFIG\SAM",
                @"\WINDOWS\SYSTEM32\CONFIG\SECURITY",
                @"\WINDOWS\SYSTEM32\CONFIG\SOFTWARE",
                @"\WINDOWS\SYSTEM32\CONFIG\SYSTEM",
                @"\WINDOWS\SYSTEM32\CONFIG\SYSTEMPROFILE\NTUSER.DAT",
            };

        protected static readonly Regex UserRegisrtyFiles = new Regex(@"^.*\\(?<parentDir>Documents and Settings|Users)\\(?<user>[^\\]+)\\ntuser.dat$", RegexOptions.IgnoreCase);

        public static bool CreateDiff(string OldVHD, string NewVHD, string Output, DiskType OutputType = DiskType.VHD)
        {
            if (File.Exists(Output)) throw new ArgumentException("Output file already exists.", "Output");
            if (!File.Exists(OldVHD)) throw new ArgumentException("Input file does not exist.", "OldVHD");
            if (!File.Exists(NewVHD)) throw new ArgumentException("Input file does not exist.", "NewVHD");

            byte[] CopyBuffer = new byte[1024*1024];
            VirtualDisk Old, New, Out;
            Old = VirtualDisk.OpenDisk(OldVHD, FileAccess.Read);
            New = VirtualDisk.OpenDisk(NewVHD, FileAccess.Read);
            Out = VirtualDisk.CreateDisk(OutputType.ToString(), VHDVarient, Output, New.Capacity, New.Geometry, null);
            using (Old) using (New) using (Out)
            {
                if (Out is DiscUtils.Vhd.Disk) ((DiscUtils.Vhd.Disk) Out).AutoCommitFooter = false;
                


            }

        }
    }
}
