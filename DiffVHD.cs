using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClrPlus.Core.Exceptions;
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
            CreateDiff(OldVHD, NewVHD, Output, OutputType, Force, Partition.HasValue ? new Tuple<int, int>(Partition.Value, Partition.Value) : null);
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
        public static void CreateDiff(string OldVHD, string NewVHD, string Output, DiskType OutputType = DiskType.VHD, bool Force = false, Tuple<int, int> Partition = null, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
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
                        Out = DiscUtils.Vhd.Disk.InitializeDynamic(fs, Ownership.None, OutputCapacity, Math.Max(New.BlockSize, 512 * 1024)); // the Max() is present only because there's currently a bug with blocksize < (8*sectorSize) in DiscUtils
                        break;
                    case DiskType.VHDX:
                        Out = DiscUtils.Vhdx.Disk.InitializeDynamic(fs, Ownership.None, OutputCapacity, Math.Max(New.BlockSize, 512 * 1024));
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
                    var OutParts = BiosPartitionTable.Initialize(Out);
                    
                    if (Partition != null)
                    {
                        OutParts.Create(GetPartitionType(Old.Partitions[Partition.Item1]), false); // there is no need (ever) for a VHD diff to have bootable partitions
                        DiffPart(DetectFileSystem(Old.Partitions[Partition.Item1]),
                                 DetectFileSystem(New.Partitions[Partition.Item2]),
                                 DetectFileSystem(OutParts[0]),  // As we made the partition spen the entire drive, it should be the only partition
                                 Style);
                    }
                    else // Partition == null
                    {
                        for (int i = 0; i < Old.Partitions.Count; i++)
                        {
                            var partIndex = OutParts.Create(GetPartitionType(Old.Partitions[i]), false);
                            DiffPart(DetectFileSystem(Old.Partitions[i]),
                                     DetectFileSystem(New.Partitions[i]),
                                     DetectFileSystem(OutParts[partIndex]),
                                     Style);
                        }

                        //////////////////
                    }


                    ////////////////
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

        private static DiscFileSystem DetectFileSystem(PartitionInfo Partition)
        {
            using (var stream = Partition.Open())
            {
                if (NtfsFileSystem.Detect(stream))
                    return new NtfsFileSystem(Partition.Open());
                stream.Seek(0, SeekOrigin.Begin);
                if (FatFileSystem.Detect(stream))
                    return new FatFileSystem(Partition.Open());

                /* Ext2/3/4 file system - when Ext becomes fully writable
                
                stream.Seek(0, SeekOrigin.Begin);
                if (ExtFileSystem.Detect(stream))
                    return new ExtFileSystem(Partition.Open());
                */

                return null;
            }
        }

        private static void DiffPart(DiscFileSystem PartA, DiscFileSystem PartB, DiscFileSystem Output, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            if (PartA == null) throw new ArgumentNullException("PartA");
            if (PartB == null) throw new ArgumentNullException("PartB");
            if (Output == null) throw new ArgumentNullException("Output");

            if (PartA is NtfsFileSystem)
            {
                ((NtfsFileSystem)PartA).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem)PartA).NtfsOptions.HideSystemFiles = false;
            }
            if (PartB is NtfsFileSystem)
            {
                ((NtfsFileSystem)PartB).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem)PartB).NtfsOptions.HideSystemFiles = false;
            }
            if (Output is NtfsFileSystem)
            {
                ((NtfsFileSystem)Output).NtfsOptions.HideHiddenFiles = false;
                ((NtfsFileSystem)Output).NtfsOptions.HideSystemFiles = false;
            }

            var RootA = PartA.Root;
            var RootB = PartB.Root;
            var OutRoot = Output.Root;

            CompareTree(RootA, RootB, OutRoot, Style);

        }

        public enum ComparisonStyle
        {
            /// <summary> For each pair of files with same name, perform DateTime compare.  If identical, continue with size and Binary compare. </summary>
            Full,
            /// <summary> Only compare filenames and sizes.  If a file exists on both sides with same size, assume identical. </summary>
            NameOnly,
            /// <summary> For each pair of files with same name, compare only DateTime and size. Does not compare content. </summary>
            DateTimeOnly,
            /// <summary> For each pair of files with same name, compares size and binary content regardless of DateTime. </summary>
            BinaryOnly,
        }
        
        private static void CompareTree(DiscDirectoryInfo A, DiscDirectoryInfo B, DiscDirectoryInfo Out,
                                        ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            var Afiles = A.GetFiles();
            foreach (var file in B.GetFiles().Where(file => !Afiles.Contains(file, new ClrPlus.Core.Extensions.EqualityComparer<DiscFileInfo>(
                                                                                       (a, b) => a.Name.Equals(b.Name),
                                                                                       d => d.Name.GetHashCode())) ||
                                                            CompareFile(A.GetFiles(file.Name).Single(), file, Style)))
            {
                CopyFile(file, Out.FileSystem.GetFileInfo(Path.Combine(Out.FullName, file.Name)), true);
            }

            var Asubs = A.GetDirectories();
            foreach (var subdir in B.GetDirectories())
            {
                if (Asubs.Contains(subdir, new ClrPlus.Core.Extensions.EqualityComparer<DiscDirectoryInfo>(
                                           (a, b) => a.Name.Equals(b.Name),
                                           d => d.Name.GetHashCode())))
                {
                    CompareTree(A.GetDirectories(subdir.Name).Single(), subdir, Out, Style);
                }
                else
                {
                    CopyTree(subdir, Out.FileSystem.GetDirectoryInfo(Path.Combine(Out.FullName, subdir.Name)), true);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="A"></param>
        /// <param name="B"></param>
        /// <param name="Style"></param>
        /// <returns>True if A and B are equivalent.</returns>
        private static bool CompareFile(DiscFileInfo A, DiscFileInfo B, ComparisonStyle Style = ComparisonStyle.DateTimeOnly)
        {
            if (A == null || B == null) return A == B;
            return A.Length == B.Length &&
                   (Style == ComparisonStyle.NameOnly || (Style == ComparisonStyle.BinaryOnly
                                                              ? FilesMatch(A, B)
                                                              : (A.LastWriteTimeUtc == B.LastWriteTimeUtc &&
                                                                 (Style == ComparisonStyle.DateTimeOnly || FilesMatch(A, B)))));
        }

        private static bool FilesMatch(DiscFileInfo A, DiscFileInfo B)
        {
            const int BufferSize = 2048;  // arbitrarily chosen buffer size
            byte[] buffA = new byte[BufferSize];
            byte[] buffB = new byte[BufferSize];

            var fileA = A.OpenRead();
            var fileB = B.OpenRead();

            int numA, numB;
            while (fileA.Position < fileA.Length)
            {
                numA = fileA.Read(buffA, 0, BufferSize);
                numB = fileB.Read(buffB, 0, BufferSize);
                if (numA != numB)
                {
                    fileA.Close();
                    fileB.Close();
                    return false;
                }
                for (int i = 0; i < numA; i++)
                    if (buffA[i] != buffB[i])
                    {
                        fileA.Close();
                        fileB.Close();
                        return false;
                    }
            }
            fileA.Close();
            fileB.Close();
            return true;
        }

        private static bool CopyTree(DiscDirectoryInfo Source, DiscDirectoryInfo Destination, bool Force)
        {
            if (!Force && Destination.Exists && Destination.GetFileSystemInfos().Any()) return false;
            if (!Source.Exists) throw new ArgumentException("Source directory does not exist.", "Source");

            bool retVal = true;
            if (!Destination.Exists) Destination.Create();
            foreach (var file in Source.GetFiles())
            {
                var DestFile = Destination.FileSystem.GetFileInfo(Path.Combine(Destination.FullName, file.Name));
                retVal &= CopyFile(file, DestFile, Force);
            }

            return retVal && Source.GetDirectories().Aggregate(true, (current, sub) => current & CopyTree(sub, Destination.FileSystem.GetDirectoryInfo(Path.Combine(Destination.FullName, sub.Name)), Force));
        }

        private static bool CopyFile(DiscFileInfo Source, DiscFileInfo Destination, bool Force)
        {
            Stream sStream, dStream;
            if (Destination.Exists)
                if (Force) dStream = Destination.Open(FileMode.Truncate, FileAccess.ReadWrite);
                else return false;
            else dStream = Destination.Create();
            using (sStream = Source.OpenRead())
            using (dStream)
                try
                {
                    sStream.CopyTo(dStream);
                }
                catch (Exception)
                {
                    return false;
                }
            return true;
        }
    }
}
