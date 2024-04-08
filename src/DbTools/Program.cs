namespace DbTools
{
    internal static class Program
    {
        private const string _scriptSuffix = "-functions-and-procedures.sql";

        static void Main(string[] args)
        {
            string migrationPath = args.Length != 0 ? args[0] : @"../../../../Storage/Migration";
            string funcAndProcDirectory = $@"{migrationPath}/FunctionsAndProcedures";
            if (!Directory.Exists(migrationPath))
            {
                throw new ArgumentException($"Migration directory {migrationPath} does not exist");
            }
            string? versionDirectory = GetVersionDirectory(migrationPath);
            if (versionDirectory == null )
            {
                return;
            }

            string scriptFile = GetScriptFile(versionDirectory);
            string cr = Environment.NewLine;
            foreach (string filename in (new DirectoryInfo(funcAndProcDirectory).GetFiles(("*.sql")).Select(f => f.FullName)))
            {
                File.AppendAllText(scriptFile, $"--{filename}:{cr}{File.ReadAllText(filename)}{cr}{cr}");
            }

            Console.WriteLine($"DbTools:{cr}Script: {scriptFile}{cr}Migration dir: {migrationPath}, {Path.GetFullPath(migrationPath)}{cr}" +
                $"Version dir: {versionDirectory}{cr}Current dir: {Directory.GetCurrentDirectory()}");
        }

        private static string? GetVersionDirectory(string migrationPath)
        {
            return new DirectoryInfo(migrationPath).GetDirectories("v*")
                .Select(d => d.FullName)
                .OrderByDescending(d => d)
                .FirstOrDefault();
        }

        private static string GetScriptFile(string versionDirectory)
        {
            FileInfo[] scriptFiles = new DirectoryInfo(versionDirectory).GetFiles($"*{_scriptSuffix}");
            if (scriptFiles.Length > 1)
            {
                throw new ArgumentException($"Multiple ({scriptFiles.Length}) potential script files found in {versionDirectory}");
            }
            else if (scriptFiles.Length == 1)
            {
                File.Delete(scriptFiles[0].FullName);
                return scriptFiles[0].FullName;
            }
            else
            {
                return $"{versionDirectory}/01{_scriptSuffix}";
            }
        }
    }
}
