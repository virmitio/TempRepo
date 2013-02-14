using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ClrPlus.Core.Collections;
using ClrPlus.Platform;

namespace VMProvisioningAgent
{

    /*
    internal class FileComparison
    {
        // This needs to be valid as part of a Windows filename, but also vanishingly unlikely to be encountered at the end of an existing filename...
        public const string SymLinkDecorator = @"%%[SYMLINK]%%";  

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
        private bool HaveCompared = false;

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
                Files = Compare(String.Empty, SourceA, SourceB, Recursive, Style);
            }
            HaveCompared = true;
        }

        private static Dictionary<string, FileCondition> Compare(string root, string sideA, string sideB, bool recurse, ComparisonStyle style)
        {
            Dictionary<string, file> Working = new Dictionary<string, file>();
            var Subs = new XDictionary<string, FileCondition>();
            
            // A-Side
            var AsideRoot = new DirectoryInfo(Path.Combine(sideA, root));
            if (AsideRoot.Exists)
            {
                var Afiles = AsideRoot.EnumerateFiles();
                foreach (FileInfo A in Afiles)
                {
                    bool bloop;
                    try
                    {
                        if (Symlink.IsSymlink(A.FullName))
                            bloop = true;

                        var tmp = A.FullName.Substring(sideA.Length);
                        if (tmp.IndexOf(Path.PathSeparator) == 0)
                            tmp = tmp.Substring(1);
                        Working.Add(tmp, new file { sideA = Symlink.IsSymlink(A.FullName) ? new FileInfo(A.FullName + SymLinkDecorator) : A });
                    }
                    catch (Exception e)
                    {
                        continue;
                    }
                }
                if (recurse)
                {
                    foreach (DirectoryInfo directory in AsideRoot.EnumerateDirectories())
                            try
                            {
                                if (Symlink.IsSymlink(directory.FullName))
                                {
                                    // treat this as a file, but decorate it in the dictionary
                                    var tmp = directory.FullName.Substring(sideA.Length) + SymLinkDecorator;
                                    if (tmp.IndexOf(Path.PathSeparator) == 0)
                                        tmp = tmp.Substring(1);
                                    Working.Add(tmp, new file { sideA = new FileInfo(directory.FullName) });
                                    continue;
                                }
                                foreach (var pair in Compare(Path.Combine(root, directory.Name), sideA, sideB, recurse, style))
                                {
                                    Subs[pair.Key] = pair.Value;
                                }
                            }
                            catch (Exception e)
                            {
                                continue;
                            }
                }
            }

            // B-Side
            var BsideRoot = new DirectoryInfo(Path.Combine(sideB, root));
            if (BsideRoot.Exists)
            {
                var Bfiles = BsideRoot.EnumerateFiles();
                foreach (FileInfo B in Bfiles)
                {
                    bool bloop;
                    try
                    {
                        if (Symlink.IsSymlink(B.FullName))
                            bloop = true;

                        var tmp = B.FullName.Substring(sideB.Length);
                        if (tmp.IndexOf(Path.PathSeparator) == 0)
                            tmp = tmp.Substring(1);
                        if (Working.ContainsKey(tmp))
                        {
                            file F = Working[tmp];
                            F.sideB = Symlink.IsSymlink(B.FullName) ? new FileInfo(B.FullName + SymLinkDecorator) : B;
                            Working[tmp] = F;
                        }
                        else
                        {
                            Working.Add(tmp, new file { sideB = Symlink.IsSymlink(B.FullName) ? new FileInfo(B.FullName + SymLinkDecorator) : B });
                        }
                    }
                    catch (Exception e)
                    {
                        continue;
                    }
                }
                if (recurse)
                {
                    var CompareFunc = new ClrPlus.Core.Extensions.EqualityComparer<DirectoryInfo>(
                        (a, b) => 
                            a.Name.Equals(b.Name),   // this is not actually used
                        d =>
                            d.Name.GetHashCode()); // This is what is REALLY used for .Except() comparison of objects

                    foreach (DirectoryInfo directory in BsideRoot.EnumerateDirectories().Except(AsideRoot.EnumerateDirectories(), CompareFunc)) // skip directories we've already covered from A-Side
                            try
                            {
                                if (Symlink.IsSymlink(directory.FullName))
                                {
                                    // treat this as a file, but decorate it in the dictionary
                                    var tmp = directory.FullName.Substring(sideB.Length) + SymLinkDecorator;
                                    if (tmp.IndexOf(Path.PathSeparator) == 0)
                                        tmp = tmp.Substring(1);
                                    if (Working.ContainsKey(tmp))
                                    {
                                        file F = Working[tmp];
                                        F.sideB = new FileInfo(directory.FullName);
                                        Working[tmp] = F;
                                    }
                                    else
                                    {
                                        Working.Add(tmp, new file { sideB = new FileInfo(directory.FullName) });
                                    }
                                    continue;
                                }
                                foreach (var pair in Compare(Path.Combine(root, directory.Name), sideA, sideB, recurse, style))
                                {
                                    Subs[pair.Key] = pair.Value;
                                }
                            }
                            catch (Exception e)
                            {
                                continue;
                            }
                }

            }

            XDictionary<string, FileCondition> ret = new XDictionary<string, FileCondition>(Working.ToDictionary(item => item.Key, item => item.Value.sideB == null ? FileCondition.OnlyA : item.Value.sideA == null ? FileCondition.OnlyB
                                                                                           // Check symlinks  (these don't get resolved to their targets for comparison, only the target pointer is checked)
                                                                                           : item.Value.sideA.FullName.EndsWith(SymLinkDecorator) ? item.Value.sideB.FullName.EndsWith(SymLinkDecorator) 
                                                                                                                                                    ? Symlink.GetActualPath(item.Value.sideA.FullName.Substring(0,item.Value.sideA.FullName.Length-SymLinkDecorator.Length)).Equals(
                                                                                                                                                      Symlink.GetActualPath(item.Value.sideB.FullName.Substring(0,item.Value.sideB.FullName.Length-SymLinkDecorator.Length)),StringComparison.InvariantCultureIgnoreCase)
                                                                                                                                                        ? FileCondition.Same : FileCondition.Diff
                                                                                                                                                    : FileCondition.Diff
                                                                                           : item.Value.sideB.FullName.EndsWith(SymLinkDecorator) ? FileCondition.Diff
                                                                                           // Check ComparisonStyle
                                                                                           : style == ComparisonStyle.NameOnly ? (item.Value.sideA.Length != item.Value.sideB.Length ? FileCondition.Diff : FileCondition.Same)
                                                                                           : style == ComparisonStyle.BinaryOnly ? (item.Value.sideA.Length != item.Value.sideB.Length
                                                                                                                                                            ? FileCondition.Diff
                                                                                                                                                            : FilesMatch(item.Value.sideA, item.Value.sideB) ? FileCondition.Same
                                                                                                                                                                                                             : FileCondition.Diff)
                                                                                           : item.Value.sideA.LastWriteTimeUtc > item.Value.sideB.LastWriteTimeUtc ? FileCondition.NewerA
                                                                                           : item.Value.sideA.LastWriteTimeUtc < item.Value.sideB.LastWriteTimeUtc ? FileCondition.NewerB
                                                                                           : item.Value.sideA.Length != item.Value.sideB.Length ? FileCondition.Diff
                                                                                           : style == ComparisonStyle.DateTimeOnly ? FileCondition.Same
                                                                                           : FilesMatch(item.Value.sideA, item.Value.sideB) ? FileCondition.Same : FileCondition.Diff));

            foreach (var pair in Subs)
                ret[pair.Key] = pair.Value;

            return new Dictionary<string, FileCondition>(ret);
        }

        private static bool FilesMatch(FileInfo A, FileInfo B)
        {
            const int BufferSize = 2048;  // arbitrarily chosen buffer size
            byte[] buffA = new byte[BufferSize];
            byte[] buffB = new byte[BufferSize];

            var fileA = A.OpenRead();
            var fileB = B.OpenRead();

            int offset = 0, numA, numB;
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
    
    internal static class FCStatics
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

        public static IEnumerable<FileInfo> DiffA(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.Diff)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceA, kvp.Key)));
        }

        public static IEnumerable<FileInfo> DiffB(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.Diff)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceB, kvp.Key)));
        }

        public static IEnumerable<FileInfo> Same(this FileComparison FC)
        {
            return
                FC.Files.Where(kvp => kvp.Value == FileComparison.FileCondition.Same)
                  .Select(kvp => new FileInfo(Path.Combine(FC.SourceA, kvp.Key)));
        }

    }
    */

}
