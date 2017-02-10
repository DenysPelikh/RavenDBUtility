using System;
using Serilog;

namespace RestoreRavenDBs
{
    class Program
    {
        private static ILogger _logger;

        static void Main(string[] args)
        {
            var configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.ColoredConsole()
                .WriteTo.File(AppDomain.CurrentDomain.BaseDirectory + "log.txt");
            _logger = configuration.CreateLogger();

            //TODO: Comming soon
            Console.WriteLine("Choose an action:");
            Console.WriteLine("1 - ");
            Console.WriteLine("2 - ");
            Console.Clear();

            var actionNumber = Convert.ToInt32(Console.ReadLine());

            switch (actionNumber)
            {
                case 1:
                    {

                        break;
                    }
                case 2:
                    {

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
