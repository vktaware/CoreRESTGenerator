using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Newtonsoft.Json;
using System;
using System.IO;

namespace VickysMicroArc
{



    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Load configuration
                var configPath = "Config.json";
                if (!File.Exists(configPath))
                {
                    Console.WriteLine("Config.json file not found!");
                    return;
                }

                var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(configPath));
                Console.WriteLine("Configuration loaded successfully.");

                // Validate inputs
                if (string.IsNullOrWhiteSpace(config.Namespace) || string.IsNullOrWhiteSpace(config.SolutionName))
                {
                    Console.WriteLine("Namespace and SolutionName must be provided in Config.json.");
                    return;
                }

                // Initialize schema reader and code generator
                var schemaReader = new DatabaseSchemaReader(config.ConnectionString);
                var generator = new CodeGenerator(config.OutputDirectory, config.Namespace, config.SolutionName,config.ConnectionString);

                // Retrieve tables and generate code
                var tables = schemaReader.GetTables();
                foreach (var table in tables)
                {
                    Console.WriteLine($"Processing table: {table.TABLE_NAME}");

                    // Get columns for the table
                    var columns = schemaReader.GetColumns(table.TABLE_NAME);

                    // Generate files
                    generator.GenerateModel(table.TABLE_NAME, columns);
                    generator.GenerateRepository(table.TABLE_NAME, columns);
                    generator.GenerateController(table.TABLE_NAME);
                }

                // Generate project and solution files
                generator.GenerateProjectFiles();

                Console.WriteLine("Code generation completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
    }

    // Configuration class to map Config.json
    public class Config
    {
        public string ConnectionString { get; set; }
        public string OutputDirectory { get; set; }
        public string Namespace { get; set; }
        public string SolutionName { get; set; }
    }


}