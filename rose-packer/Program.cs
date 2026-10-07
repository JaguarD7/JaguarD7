using System;
using System.IO;
using CodeWalker.GameFiles;

internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: RoseRpfPacker <source-folder> <output.rpf>");
                return 2;
            }

            string source = Path.GetFullPath(args[0]);
            string output = Path.GetFullPath(args[1]);
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException(source);

            if (!File.Exists(Path.Combine(source, "assembly.xml")))
                throw new FileNotFoundException("assembly.xml is required at the RPF root.");

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            if (File.Exists(output)) File.Delete(output);

            var rpf = RpfFile.CreateNew(Path.GetDirectoryName(output)!, Path.GetFileName(output), RpfEncryption.OPEN);
            AddTree(rpf.Root, source);

            var verify = new RpfFile(output, Path.GetFileName(output));
            using (var fs = File.OpenRead(output))
            using (var br = new BinaryReader(fs))
            {
                verify.ScanStructure(null, null);
            }

            bool hasAssembly = false;
            foreach (var f in verify.Root.Files)
            {
                if (string.Equals(f.Name, "assembly.xml", StringComparison.OrdinalIgnoreCase))
                {
                    hasAssembly = true;
                    break;
                }
            }
            if (!hasAssembly) throw new Exception("RPF validation failed: assembly.xml missing after pack.");

            Console.WriteLine($"OK {output} {new FileInfo(output).Length} bytes");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    static void AddTree(RpfDirectoryEntry target, string source)
    {
        foreach (string dir in Directory.GetDirectories(source))
        {
            string name = Path.GetFileName(dir);
            var child = RpfFile.CreateDirectory(target, name);
            AddTree(child, dir);
        }

        foreach (string file in Directory.GetFiles(source))
        {
            string name = Path.GetFileName(file);
            byte[] data = File.ReadAllBytes(file);
            RpfFile.CreateFile(target, name, data, true);
        }
    }
}