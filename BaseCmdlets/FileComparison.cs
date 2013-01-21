using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VMProvisioningAgent
{
    public class FileComparison
    {
        public enum ComparisonStyle
        {
            /// <summary> For each pair of files with same name, perform DateTime compare.  If identical, continue with size and Binary compare. </summary>
            Normal,
            /// <summary> Only compare filenames and sizes.  If a file exists on both sides with same size, assume identical. </summary>
            NameOnly,
            /// <summary> For each pair of files with same name, compare only DateTime and size. Does not compare content. </summary>
            DateTimeOnly,
            /// <summary> For each pair of files with same name, compares size and binary content regardless of DateTime. </summary>
            BinaryOnly,
        }

        public enum FileCondition
        {
            Same,
            OnlyA,
            NewerA,
            OnlyB,
            NewerB,
            Diff,
        }

        public string SourceA { get; private set; }
        public string SourceB { get; private set; }
        public bool Recursive { get; private set; }
        public Dictionary<string, FileCondition> Files { get; private set; }
        private bool HaveCompared;

        public FileComparison(string SideA, string SideB, bool RecurseDirectories)
        {
            SourceA = SideA;
            SourceB = SideB;
            Recursive = RecurseDirectories;
            Files = new Dictionary<string, FileCondition>();
        }

        /// <summary>
        /// Performs the actual comparison.
        /// </summary>
        /// <param name="Style">Comparison style to be used</param>
        /// <param name="Force">If True, will clear any existing results and perform a fresh comparison.  If False, will not compare again if a comparison has already been performed.  Defaults to False.</param>
        public void DoCompare(ComparisonStyle Style, bool Force = false)
        {
            if (!HaveCompared || Force)
            {
                Files.Clear();
                Files = Compare(String.Empty, SourceA, SourceB, Recursive);
            }
            HaveCompared = true;
        }

        private static Dictionary<string, FileCondition> Compare(string root, string sideA, string sideB, bool recurse, ComparisonStyle style)
        {
            Dictionary<string, file> Working = new Dictionary<string, file>();
            
            // A-Side
            var AsideRoot = new DirectoryInfo(Path.Combine(sideA, root));
            var Afiles = AsideRoot.GetFiles("*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (FileInfo A in Afiles)
            {
                var tmp = A.FullName.Substring(sideA.Length);
                if (tmp.IndexOf(Path.PathSeparator) == 0)
                    tmp = tmp.Substring(1);
                Working.Add(tmp, new file{sideA = A});
            }

            // B-Side
            var BsideRoot = new DirectoryInfo(Path.Combine(sideB, root));
            var Bfiles = BsideRoot.GetFiles("*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (FileInfo B in Bfiles)
            {
                var tmp = B.FullName.Substring(sideB.Length);
                if (tmp.IndexOf(Path.PathSeparator) == 0)
                    tmp = tmp.Substring(1);
                if (Working.ContainsKey(tmp))
                {
                    file F = Working[tmp];
                    F.sideB = B;
                    Working[tmp] = F;
                }
                else
                {
                    Working.Add(tmp, new file {sideB = B});
                }
            }

            return Working.ToDictionary(item => item.Key, item => item.Value.sideB == null ? FileCondition.OnlyA : item.Value.sideA == null ? FileCondition.OnlyB 
                                                                                           // Check ComparisonStyle
                                                                                           : style == ComparisonStyle.NameOnly ? (item.Value.sideA.Length != item.Value.sideB.Length ? FileCondition.Diff : FileCondition.Same) 
                                                                                           : style == ComparisonStyle.BinaryOnly ? (item.Value.sideA.Length != item.Value.sideB.Length ? FileCondition.Diff : FilesMatch(item.Value.sideA, item.Value.sideB) ? FileCondition.Same : FileCondition.Diff) 
                                                                                           : item.Value.sideA.LastWriteTimeUtc > item.Value.sideB.LastWriteTimeUtc ? FileCondition.NewerA 
                                                                                           : item.Value.sideA.LastWriteTimeUtc < item.Value.sideB.LastWriteTimeUtc ? FileCondition.NewerB 
                                                                                           : item.Value.sideA.Length != item.Value.sideB.Length ? FileCondition.Diff 
                                                                                           : style == ComparisonStyle.DateTimeOnly ? FileCondition.Same 
                                                                                           : FilesMatch(item.Value.sideA, item.Value.sideB) ? FileCondition.Same : FileCondition.Diff);
        }

        private static bool FilesMatch(FileInfo A, FileInfo B)
        {
            const int BufferSize = 256;
            byte[] buffA = new byte[BufferSize];
            byte[] buffB = new byte[BufferSize];

            var fileA = A.OpenRead();
            var fileB = B.OpenRead();

            int offset = 0, numA, numB;
            while (fileA.Position < fileA.Length)
            {
                numA = fileA.Read(buffA, offset, BufferSize);
                numB = fileB.Read(buffB, offset, BufferSize);
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

                offset += numA;
            }
            fileA.Close();
            fileB.Close();
            return true;
        }
        private struct file
        {
            public FileInfo sideA;
            public FileInfo sideB;
        }

    }
    public static class FCStatics
    {
        public static IEnumerable<FileInfo> OnlyA(this FileComparison FC)
        {
            return 
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.OnlyA)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceA, kvp.Key)));
        }

        public static IEnumerable<FileInfo> OnlyB(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.OnlyB)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceB, kvp.Key)));
        }

        public static IEnumerable<FileInfo> NewerA(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.NewerA)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceA, kvp.Key)));
        }

        public static IEnumerable<FileInfo> NewerB(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.NewerB)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceB, kvp.Key)));
        }

        public static IEnumerable<FileInfo> Different(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.Diff)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceA, kvp.Key)));
        }

    }
}
