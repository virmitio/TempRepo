using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;

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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="OldVHD"></param>
        /// <param name="NewVHD"></param>
        /// <param name="Output">Filename to the output file.  Method will fail if this already exists unless Force is passed as 'true'.</param>
        /// <param name="OutputType">A <see cref="VMProvisioningAgent.DiffVHD.DiskType"/> which specifies the output file format.</param>
        /// <param name="Force">If true, will overwrite the Output file if it already exists.  Defaults to 'false'.</param>
        /// <param name="Partition">The 0-indexed partition number to compare from each disk file.</param>
        /// <returns></returns>
        public static void CreateDiff(string OldVHD, string NewVHD, string Output, DiskType OutputType = DiskType.VHD, bool Force = false, int? Partition = null)
        {
            return CreateDiff(OldVHD, NewVHD, Output, OutputType, Force, Partition.HasValue ? new Tuple<int, int>(Partition.Value, Partition.Value) : null);
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="OldVHD"></param>
        /// <param name="NewVHD"></param>
        /// <param name="Output">Filename to the output file.  Method will fail if this already exists unless Force is passed as 'true'.</param>
        /// <param name="OutputType">A <see cref="VMProvisioningAgent.DiffVHD.DiskType"/> which specifies the output file format.</param>
        /// <param name="Force">If true, will overwrite the Output file if it already exists.  Defaults to 'false'.</param>
        /// <param name="Partition">An int tuple which declares a specific pair of partitions to compare.  The first value in the tuple will be the 0-indexed partition number from OldVHD to compare against.  The second value of the tuple will be the 0-indexed parition from NewVHD to compare with.</param>
        /// <returns></returns>
        public static void CreateDiff(string OldVHD, string NewVHD, string Output, DiskType OutputType = DiskType.VHD, bool Force = false, Tuple<int, int> Partition = null)
        {
            if (File.Exists(Output) && !Force) throw new ArgumentException("Output file already exists.", "Output");
            if (!File.Exists(OldVHD)) throw new ArgumentException("Input file does not exist.", "OldVHD");
            if (!File.Exists(NewVHD)) throw new ArgumentException("Input file does not exist.", "NewVHD");

            byte[] CopyBuffer = new byte[1024*1024];
            VirtualDisk Old, New, Out;
            Old = VirtualDisk.OpenDisk(OldVHD, FileAccess.Read);
            New = VirtualDisk.OpenDisk(NewVHD, FileAccess.Read);

            using (Old)
            using (New)
            using (var fs = new FileStream(OldVHD, Force ? FileMode.Create : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {

                // Check type of filesystems being compared
                if (!Old.IsPartitioned) throw new ArgumentException("Input disk is not partitioned.", "OldVHD");
                if (!New.IsPartitioned) throw new ArgumentException("Input disk is not partitioned.", "NewVHD");

                long CapacityBuffer = 64 * Math.Max(Old.Geometry.BytesPerSector, New.Geometry.BytesPerSector); // starting with 64 sectors as a buffer for partition information in the output file
                long[] OutputCapacities = new long[Partition != null ? 1 : Old.Partitions.Count];

                if (Partition != null)
                {
                    var PartA = Old.Partitions[Partition.Item1];
                    var PartB = New.Partitions[Partition.Item2];
                    if (PartA.BiosType != PartB.BiosType)
                        throw new InvalidFileSystemException(
                            String.Format(
                                "Filesystem of partition {0} in '{1}' does not match filesystem type of partition {2} in '{3}'.",
                                Partition.Item2, NewVHD, Partition.Item1, OldVHD));
                    OutputCapacities[0] += Math.Max(PartA.SectorCount * Old.Geometry.BytesPerSector, PartB.SectorCount * New.Geometry.BytesPerSector);
                }
                else
                {
                    if (Old.Partitions.Count != New.Partitions.Count)
                        throw new ArgumentException(
                            "Input disks do not have the same number of partitions.  To compare specific partitions on mismatched disks, provide the 'Partition' parameter.");
                    for (int i = 0; i < Old.Partitions.Count; i++)
                        if (Old.Partitions[i].BiosType != New.Partitions[i].BiosType)
                            throw new InvalidFileSystemException(String.Format("Filesystem of partition {0} in '{1}' does not match filesystem type of partition {0} in '{2}'.", i, NewVHD, OldVHD));
                        else
                            OutputCapacities[i] = Math.Max(Old.Partitions[i].SectorCount * Old.Geometry.BytesPerSector, New.Partitions[i].SectorCount * New.Geometry.BytesPerSector);
                }


                long OutputCapacity = CapacityBuffer + OutputCapacities.Sum();

                switch (OutputType)
                {
                    case DiskType.VHD:
                        Out = DiscUtils.Vhd.Disk.InitializeDynamic(fs, Ownership.None, OutputCapacity, Math.Max(New.BlockSize, 4096)); // the Max() is present only because there's currently a bug with blocksize < (8*sectorSize) in DiscUtils
                        break;
                    case DiskType.VHDX:
                        Out = DiscUtils.Vhdx.Disk.InitializeDynamic(fs, Ownership.None, OutputCapacity, Math.Max(New.BlockSize, 4096));
                        break;
                    default:
                        throw new NotSupportedException("The selected disk type is not supported at this time.",
                                                        new ArgumentException(
                                                            "Selected DiskType not currently supported.", "OutputType"));
                }



                using (Out)
                {

                    // set up the output location
                    if (Out is DiscUtils.Vhd.Disk) ((DiscUtils.Vhd.Disk) Out).AutoCommitFooter = false;
                    var OutParts = DiscUtils.Partitions.BiosPartitionTable.Initialize(Out);
                    
                    if (Partition != null)
                    {
                        OutParts.Create(GetPartitionType(Old.Partitions[Partition.Item1]), false); // there is no need (ever) for a VHD diff to have bootable partitions

                    }
                    else // Partition == null
                    {
                        
                    }



                }

            }
        }

        private static WellKnownPartitionType GetPartitionType(PartitionInfo Partition)
        {
            switch (Partition.BiosType)
            {
                case BiosPartitionTypes.Fat16:
                case BiosPartitionTypes.Fat32:
                case BiosPartitionTypes.Fat32Lba:
                    return WellKnownPartitionType.WindowsFat;
                case BiosPartitionTypes.Ntfs:
                    return WellKnownPartitionType.WindowsNtfs;
                case BiosPartitionTypes.LinuxNative:
                    return WellKnownPartitionType.Linux;
                case BiosPartitionTypes.LinuxSwap:
                    return WellKnownPartitionType.LinuxSwap;
                case BiosPartitionTypes.LinuxLvm:
                    return WellKnownPartitionType.LinuxLvm;
                default:
                    throw new ArgumentException(
                        String.Format("Unsupported partition type: '{0}'", BiosPartitionTypes.ToString(Partition.BiosType)), "Partition");
            }
        }

        private DiscFileSystem DetectFileSystem(PartitionInfo Partition)
        {
            using (var stream = Partition.Open())
            {
                if (NtfsFileSystem.Detect(stream))
                    return new NtfsFileSystem(Partition.Open());
                stream.Seek(0, SeekOrigin.Begin);
                if (FatFileSystem.Detect(stream))
                    return new FatFileSystem(Partition.Open());
                stream.Seek(0, SeekOrigin.Begin);

            }
        }

        private void DiffPart()
        {
            
        }

    }
}
