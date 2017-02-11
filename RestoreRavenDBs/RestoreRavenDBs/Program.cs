using System;
using System.Configuration;
using Raven.Client.Document;
using RestoreRavenDBs.Handlers;
using Serilog;

namespace RestoreRavenDBs
{
    class Program
    {
        static void Main(string[] args)
        {
            var configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.ColoredConsole()
                .WriteTo.File(AppDomain.CurrentDomain.BaseDirectory + "log.txt");
            var logger = configuration.CreateLogger();

            var ravenUrl = ConfigurationManager.AppSettings["RavenDbUrl"];

            var store = new DocumentStore
            {
                Url = ravenUrl
            };
            store.Initialize();

            var restoreRavenDbHandler = new RestoreRavenDbHandler(store, logger);

            Console.WriteLine("Choose an action:");
            Console.WriteLine("1 - Smuggler Full Export");
            Console.WriteLine("2 - Smuggler Full Import");
            Console.WriteLine("3 - Smuggler Full Export specific database");
            Console.WriteLine("4 - Smuggler Full Import specific database");
            var actionNumber = Convert.ToInt32(Console.ReadLine());
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
