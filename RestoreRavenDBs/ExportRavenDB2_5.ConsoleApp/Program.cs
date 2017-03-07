using System;
using System.Configuration;
using ExportRavenDB2_5.Common;
using ExportRavenDB2_5.Handlers;
using Raven.Client.Document;
using Serilog;

namespace ExportRavenDB2_5.ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.ColoredConsole()
                .WriteTo.RollingFile(AppDomain.CurrentDomain.BaseDirectory + "\\logs\\log-{Date}.txt");
            var logger = configuration.CreateLogger();

            var ravenUrl = ConfigurationManager.AppSettings["RavenDbUrl"];

            var store = new DocumentStore
            {
                Url = ravenUrl
            };
            store.Initialize();

            var backupDir = ConfigurationManager.AppSettings["DefaultBackupDir"];

            var smugglerWrapper = new SmugglerWrapper2_5(store, logger);
            var exportRavenDbHandler = new ExportRavenDbHandler2_5(store, logger, smugglerWrapper, backupDir);

            Console.WriteLine("Choose an action:");
            Console.WriteLine("1 - Smuggler Full Export");
            Console.WriteLine("2 - Smuggler Full Export specific database");
            int actionNumber;
            int.TryParse(Console.ReadLine(), out actionNumber);
            Console.Clear();

            switch (actionNumber)
            {
                case 1:
                    {
                        exportRavenDbHandler.SmugglerFullExport();
                        break;
                    }
                case 2:
                    {
                        Console.WriteLine("Enter the name of the database");
                        var databaseName = Console.ReadLine();

                        exportRavenDbHandler.SmugglerFullExport(databaseName);
                        break;
                    }
                    
                default:
                    {
                        Console.WriteLine("Incorrect");
                        break;
                    }
            }

            Console.ReadLine();
        }
    }
}
