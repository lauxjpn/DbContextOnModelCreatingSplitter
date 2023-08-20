using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CommandLine;

namespace DbContextOnModelCreatingSplitter
{
   public class Options
   {
      [Option('c', "dbcontext", Required = true, HelpText = "Path the the DbContext file")]
      public string DbContextPath { get; set; }

      [Option('o', "outdir", Required = false, HelpText = "Output path for the generated configClass files")]
      public string OutputDirectoryPath { get; set; }

      [Option('n', "namespace", Required = false, HelpText = "Namespace for the generated configClass classes")]
      public string Namespace { get; set; }

      [Option('B', "no-backup", Required = false, HelpText = "Don't keep a copy of the original DbContext file")]
      public bool NoBackup { get; set; }

      [Option('r', "no-replace", Default = false, Required = false, HelpText = "Don't replace OnModelCreating event code")]
      public bool NoReplacement { get; set; }

      [Option('e', "embed-config", Default = false, Required = false, HelpText = "Embed EntityTypeConfiguration class into entity model file")]
      public bool EmbedConfigClass { get; set; }
   }

   class Program
   {
      static short _indentSizeSource = 4;
      static short _indentSize = 4;

      static void Main(string[] args)
      {
         Parser.Default.ParseArguments<Options>(args)
             .WithParsed(Run);
      }

      private static void Run(Options options)
      {
         var dbContextFilePath = Path.GetFullPath(options.DbContextPath);
         var configurationsDirPath = !string.IsNullOrEmpty(options.OutputDirectoryPath) ? Path.GetFullPath(options.OutputDirectoryPath) : Path.GetDirectoryName(dbContextFilePath);

         var source = File.ReadAllText(dbContextFilePath);

         Directory.CreateDirectory(configurationsDirPath);

         var contextUsingStatements = Regex.Matches(source, @"^using\s+.*?;", RegexOptions.Multiline | RegexOptions.Singleline)
             .Select(m => m.Value)
             .ToList();

         var contextNamespace = Regex.Match(source,
                                            @"(?<=(?:^|\s|;)namespace\s+).*?(?=(?:;|\s|\{))",
                                            RegexOptions.Multiline | RegexOptions.Singleline).Value;

         var configurationNamespace = options.Namespace ?? contextNamespace;

         Console.WriteLine("Create configClass files:");

         string modelsBackupDirPath = "";
         if (!options.NoBackup & options.EmbedConfigClass)
         {
            var modelsBackupDir = $"_backup_{DateTime.Now:yyyyMMddHHmmss}";
            modelsBackupDirPath = Path.Combine(configurationsDirPath, modelsBackupDir);
            Directory.CreateDirectory(modelsBackupDirPath);
         }

         const string statementsOuterBlockPattern = @"\s*modelBuilder\.Entity<.*?>\(.*?\s*=>\s*\{.*?\r?\n\s*\}\);\r?\n";
         const string statementsInnerBlockPattern = @"(?<=modelBuilder\.Entity<(?<EntityName>.*?)>\((?<EntityParameterName>.*?)\s*=>\s*\{\r?\n).*?(?=\r?\n\s*\}\);)";

         var statementsBlockMatches = Regex.Matches(source, statementsOuterBlockPattern, RegexOptions.Multiline | RegexOptions.Singleline).ToList();

         foreach (var blockMatch in statementsBlockMatches)
         {
            var innerBlock = Regex.Match(blockMatch.Value, statementsInnerBlockPattern, RegexOptions.Multiline | RegexOptions.Singleline);

            var entityMaybeFullName = innerBlock.Groups["EntityName"].Value;
            var entityName = entityMaybeFullName.Substring(entityMaybeFullName.LastIndexOf('.') + 1);
            var entityParameterName = innerBlock.Groups["EntityParameterName"].Value;

            var statements = innerBlock.Value;
            var indentAtStatementsFirstLine = Regex.Match(statements, @"^\s+").Value;

            short tabs = 0;
            if (!options.EmbedConfigClass)
               tabs = 1;

            var configClass = new StringBuilder();
            configClass.AppendLine(Tab(tabs) + $"public class {entityName}Configuration : IEntityTypeConfiguration<{entityMaybeFullName}>");
            configClass.AppendLine(Tab(tabs) + "{");
            tabs++;
            configClass.AppendLine(Tab(tabs) + $"public void Configure(EntityTypeBuilder<{entityMaybeFullName}> {entityParameterName})");
            configClass.AppendLine(Tab(tabs) + "{");
            tabs++;
            configClass.AppendLine(statements.Replace(indentAtStatementsFirstLine, Tab(tabs)));
            tabs--;
            configClass.AppendLine(Tab(tabs) + "}");
            tabs--;
            configClass.AppendLine(Tab(tabs) + "}");

            string configurationFilePath;
            if (options.EmbedConfigClass)
            {
               configurationFilePath = Path.Combine(configurationsDirPath, $"{entityName}.cs");
               var modelContents = File.ReadAllText(configurationFilePath);

               var usingBlockMatches = Regex.Matches(modelContents, "^using .*?;\r?\n", RegexOptions.Multiline | RegexOptions.Singleline);
               var lastUsingLine = usingBlockMatches.Last().Value;
               var modifiedUsingBlock = lastUsingLine +
                                        "using Microsoft.EntityFrameworkCore;" + Environment.NewLine +
                                        "using Microsoft.EntityFrameworkCore.Metadata.Builders;" + Environment.NewLine;
               modelContents = modelContents.Replace(lastUsingLine, modifiedUsingBlock);
               modelContents += Environment.NewLine + configClass.ToString();

               if (!options.NoBackup)
               {
                  var backupFilePath = Path.Combine(modelsBackupDirPath, Path.GetFileName(configurationFilePath));
                  File.Copy(configurationFilePath, backupFilePath, true);
               }

               File.WriteAllText(configurationFilePath, modelContents);
            }
            else
            {
               var configFile = new StringBuilder();
               configFile.AppendLine(string.Join(Environment.NewLine, contextUsingStatements));
               configFile.AppendLine("using Microsoft.EntityFrameworkCore.Metadata.Builders;");
               configFile.AppendLine($"using {contextNamespace};");
               configFile.AppendLine();
               configFile.AppendLine($"namespace {configurationNamespace}");
               configFile.AppendLine("{");
               configFile.AppendLine(configClass.ToString());
               configFile.AppendLine("}");

               configurationFilePath = Path.Combine(configurationsDirPath, $"{entityName}Configuration.cs");
               File.WriteAllText(configurationFilePath, configFile.ToString());
            }

            Console.WriteLine(TabSrc(1) + configurationFilePath);

            string srcEntityConfigLine = "";
            if (!options.NoReplacement)
               srcEntityConfigLine = Environment.NewLine + TabSrc(2) +
                                     $"new {entityName}Configuration().Configure(modelBuilder.Entity<{entityMaybeFullName}>());";
            source = source.Replace(blockMatch.Value, srcEntityConfigLine);
         }

         if (!statementsBlockMatches.Any())
         {
            Console.WriteLine(TabSrc(1) + "No entity definitions found.");
            return;
         }

         if (!options.NoBackup)
         {
            if (options.EmbedConfigClass)
               Console.WriteLine($"Backup models files saved at: {modelsBackupDirPath}");
            Console.WriteLine();

            var backupFilePath = Path.Combine(Path.GetDirectoryName(dbContextFilePath), $"{Path.GetFileName(dbContextFilePath)}.{DateTime.Now:yyyyMMddHHmmss}.bak");
            Console.WriteLine("Backup DbContext file:");
            Console.WriteLine(TabSrc(1) + $"Original file path: {dbContextFilePath}");
            Console.WriteLine(TabSrc(1) + $"Backup file path: {backupFilePath}");
            File.Copy(dbContextFilePath, backupFilePath, true);
         }

         File.WriteAllText(dbContextFilePath, source);
      }

      private static string TabSrc(short tabsNumber)
      {
         return new string(' ', _indentSizeSource * tabsNumber); ;
      }

      private static string Tab(short tabsNumber)
      {
         return new string(' ', _indentSize * tabsNumber); ;
      }

   }
}