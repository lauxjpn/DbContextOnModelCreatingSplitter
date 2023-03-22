using CommandLine;
using System.Text;
using System.Text.RegularExpressions;

namespace DbContextOnModelCreatingSplitter
{
    public class Options
    {
        [Option('c', "dbcontext", Required = true, HelpText = "Path the the DbContext file")]
        public string DbContextPath { get; set; } = null!;

        [Option('o', "outdir", Required = false, HelpText = "Output path for the generated configuration files")]
        public string? OutputDirectoryPath { get; set; }

        [Option('n', "namespace", Required = false, HelpText = "Namespace for the generated configuration classes")]
        public string? Namespace { get; set; }

        [Option('s', "suffix", Required = false, HelpText = "Suffix for the generated configuration files (Default: Configuration)")]
        public string? Suffix { get; set; }

        [Option('B', "no-backup", Required = false, HelpText = "Don't keep a copy of the original DbContext file")]
        public bool NoBackup { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(Run);
        }

        private static void Run(Options options)
        {
            var dbContextFilePath = Path.GetFullPath(options.DbContextPath);
            string dbContexFolder = Path.GetDirectoryName(dbContextFilePath) ?? throw new NullReferenceException();
            var configurationsDirectoryPath = options.OutputDirectoryPath != null ? Path.GetFullPath(options.OutputDirectoryPath) : dbContexFolder;

            var source = File.ReadAllText(dbContextFilePath);

            Directory.CreateDirectory(configurationsDirectoryPath);

            var contextUsingStatements = Regex.Matches(source, @"^using\s+.*?;", RegexOptions.Multiline | RegexOptions.Singleline)
                .Select(m => m.Value)
                .ToList();

            string contextNamespace = Regex.Match(source, @"(?<=(?:^|\s|;)namespace\s+).*?(?=(?:\s|\{))", RegexOptions.Multiline | RegexOptions.Singleline).Value;

            var configurationNamespace = options.Namespace ?? contextNamespace;

            const string statementsInnerBlockPattern = @"(?<=modelBuilder\.Entity<(?<EntityName>.*?)>\((?<EntityParameterName>.*?)\s*=>\s*\{).*?(?:;)(?=\r?\n\s*\}\);)";
            //const string statementsInnerBlockPattern = @"(?<=modelBuilder\.Entity<(?<EntityName>.*?)>\((?<EntityParameterName>.*?)\s*=>\s*\{).*?(?:;)(?=\s*\}\);)";

            var statementsBlockMatches = Regex.Matches(source, statementsInnerBlockPattern, RegexOptions.Multiline | RegexOptions.Singleline)
                .ToList();

            Console.WriteLine("Create configuration files:");

            foreach (var blockMatch in statementsBlockMatches)
            {
                var entityName = blockMatch.Groups["EntityName"].Value;
                var entityParameterName = blockMatch.Groups["EntityParameterName"].Value;
                var statements = Regex.Replace(blockMatch.Value, @"^\t+", new string(' ', 4), RegexOptions.Multiline)
                    .TrimStart('\r', '\n', '\t', ' ')
                    .Replace(new string(' ', 16), new string(' ', 12));

                string suffix = options.Suffix ?? "Configuration";

                var configuration = new StringBuilder();

                configuration.AppendLine(string.Join(Environment.NewLine, contextUsingStatements));
                configuration.AppendLine("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
                configuration.AppendLine($"using {contextNamespace};");
                configuration.AppendLine();
                configuration.AppendLine($"namespace {configurationNamespace}");
                configuration.AppendLine("{");
                configuration.AppendLine(new string(' ', 4) + $"public class {entityName}{suffix} : IEntityTypeConfiguration<{entityName}>");
                configuration.AppendLine(new string(' ', 4) + "{");
                configuration.AppendLine(new string(' ', 8) + $"public void Configure(EntityTypeBuilder<{entityName}> {entityParameterName})");
                configuration.AppendLine(new string(' ', 8) + "{");
                configuration.AppendLine(new string(' ', 12) + statements);
                configuration.AppendLine(new string(' ', 8) + "}");
                configuration.AppendLine(new string(' ', 4) + "}");
                configuration.AppendLine("}");

                var configurationContents = configuration.ToString();
                var configurationFilePath = Path.Combine(configurationsDirectoryPath, $"{entityName}{suffix}.cs");

                Console.WriteLine(new string(' ', 4) + configurationFilePath);

                File.WriteAllText(configurationFilePath, configurationContents);
            }

            if (!statementsBlockMatches.Any())
            {
                Console.WriteLine(new string(' ', 4) + "No entity definitions found.");
                return;
            }

            const string statementsOuterBlockPattern = @"\s*modelBuilder\.Entity<.*?>\(.*?\s*=>\s*\{.*?;?.*\}\);";

            var netcontent = new StringBuilder();
            netcontent.AppendLine();
            netcontent.AppendLine();
            foreach (var blockMatch in statementsBlockMatches)
            {
                var entityName = blockMatch.Groups["EntityName"].Value;
                string suffix = options.Suffix ?? "Configuration";

                netcontent.AppendLine(new string(' ', 12) + $"modelBuilder.ApplyConfiguration(new {entityName}{suffix}());");
            }
            var newConfigurationContents = netcontent.ToString(); //string.Empty

            source = Regex.Replace(source, statementsOuterBlockPattern, newConfigurationContents, RegexOptions.Multiline | RegexOptions.Singleline);



            if (!options.NoBackup)
            {
                Console.WriteLine("Backup DbContext file:");

                var backupFilePath = Path.Combine(dbContexFolder, $"{Path.GetFileName(dbContextFilePath)}.{DateTime.Now:yyyyMMddHHmmss}.bak");

                Console.WriteLine(new string(' ', 4) + "Original file path:" + dbContextFilePath);
                Console.WriteLine(new string(' ', 4) + "Backup file path:" + backupFilePath);

                File.Copy(dbContextFilePath, backupFilePath, true);
            }

            File.WriteAllText(dbContextFilePath, source);
        }
    }
}
