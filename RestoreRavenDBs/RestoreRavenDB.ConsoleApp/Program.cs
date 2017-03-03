using System;
using Raven.Client.Document;
using Serilog;
using System.Configuration;
using RestoreRavenDB.Common;
using RestoreRavenDB.Handlers;

namespace RestoreRavenDB.ConsoleApp
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

            var smugglerWrapper = new SmugglerWrapper(store, logger);
            var restoreRavenDbHandler = new RestoreRavenDbHandler(store, logger, smugglerWrapper, backupDir);

            Console.WriteLine("Choose an action:");
            Console.WriteLine("1 - Smuggler Full Export");
            Console.WriteLine("2 - Smuggler Full Import");
            Console.WriteLine("3 - Smuggler Full Export specific database");
            Console.WriteLine("4 - Smuggler Full Import specific database");
            int actionNumber;
            int.TryParse(Console.ReadLine(), out actionNumber);
            Console.Clear();

            switch (actionNumber)
            {
                case 1:
                    {
                        restoreRavenDbHandler.SmugglerFullExport();
                        break;
                    }
                case 2:
                    {
                        restoreRavenDbHandler.SmugglerFullImport();
                        break;
                    }
                case 3:
                    {
                        Console.WriteLine("Enter the name of the database");
                        var databaseName = Console.ReadLine();

                        restoreRavenDbHandler.SmugglerFullExport(databaseName);
                        break;
                    }
                case 4:
                    {
                        Console.WriteLine("Enter the name of the database");
                        var databaseName = Console.ReadLine();

                        restoreRavenDbHandler.SmugglerFullImport(databaseName);
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
