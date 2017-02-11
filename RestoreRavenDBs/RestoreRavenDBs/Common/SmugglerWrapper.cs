using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Abstractions.Smuggler;
using Raven.Client;
using Raven.Smuggler;
using Serilog;

namespace RestoreRavenDBs.Common
{
    public class SmugglerWrapper : ISmugglerWrapper
    {
        private readonly IDocumentStore _store;
        private readonly ILogger _logger;

        private readonly double _breakTimeSeconds;
        private readonly string _ravenDumpExtension;

        public SmugglerWrapper(IDocumentStore store, ILogger logger)
        {
            _store = store;
            _logger = logger;

            _ravenDumpExtension = ".ravendump";
            _breakTimeSeconds = 5;
        }

        //We use Console Process and Smuggler.exe 3.5 for this
        public void ExportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments)
        {
            _logger.Information("Backing up database " + databaseName);

            var fileName = Path.ChangeExtension(databaseName, _ravenDumpExtension);

            var actionPath = $"out {_store.Url}databases/ ";

            var smugglerPath = AppDomain.CurrentDomain.BaseDirectory + @"Raven.Smuggler.3.5.exe";
            var smugglerArgs = string.Concat(actionPath, databaseName, fileName, additionalSmugglerArguments);

            try
            {
                var exitCode = StartSmugglerProcess(smugglerPath, smugglerArgs);

                //TODO probably need to add this event or something and alos consider other way
                if (exitCode != 0)
                {
                    _logger.Warning($"Process {smugglerPath} didn't work with arguments {smugglerArgs}");
                }
                else
                {
                    _logger.Information($"Export the database {databaseName} was successful");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error occurred while trying to export {databaseName} with exception: {ex}");
            }

            Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));
        }

        //TODO: current version RavenDb Client 3.5, we use Smuggler.exe 3.0 because Smuggler.exe 3.5 does not exist SmugglerApi
        public void ExportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToExport = ItemType.Documents)
        {
            var fileName = Path.ChangeExtension(databaseName, _ravenDumpExtension);

            var smugglerApi = new SmugglerDatabaseApi(new SmugglerDatabaseOptions
            {
                OperateOnTypes = itemTypeToExport,
                Incremental = false
            });

            var exportOptions = new SmugglerExportOptions<RavenConnectionStringOptions>
            {
                ToFile = fileName,
                From = new RavenConnectionStringOptions
                {
                    DefaultDatabase = databaseName,
                    Url = _store.Url
                }
            };

            //TODO: consider this
            var operationState = smugglerApi.ExportData(exportOptions).Result;
        }

        //We use Console Process and Smuggler.exe 3.5 for this
        public void ImportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments)
        {
            _logger.Information("The database to restore = {0}", databaseName);

            var fileName = Path.ChangeExtension(databaseName, _ravenDumpExtension);

            var actionPath = $"in {_store.Url} ";

            var smugglerPath = AppDomain.CurrentDomain.BaseDirectory + @"Raven.Smuggler.3.5.exe";
            var smugglerArgs = string.Concat(actionPath,
                fileName, " --database=", databaseName,
                " --negative-metadata-filter:@id=Raven/Encryption/Verification",
                additionalSmugglerArguments);

            try
            {
                var exitCode = StartSmugglerProcess(smugglerPath, smugglerArgs);

                // if we have fail, we try do it again
                if (exitCode != 0)
                {
                    _logger.Warning("Smuggler failed the first time");
                    _logger.Warning($"Sleeping for {_breakTimeSeconds} seconds before trying again to backup {databaseName}");
                    Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));

                    _logger.Information("Trying to export again");

                    var exitCodeTry = StartSmugglerProcess(smugglerPath, smugglerArgs);
                    if (exitCodeTry != 0)
                    {
                        //TODO probably need to add this event or something
                        throw new Exception($"Process {smugglerPath} didn't work with arguments {smugglerArgs}");
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));
                    _logger.Information($"Succeeded the second time for {databaseName}");
                }
                else
                    Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error occurred while trying to backup {databaseName} with exception: {ex}");
            }
        }

        //TODO: current version RavenDb Client 3.5, we use Smuggler.exe 3.0 because Smuggler.exe 3.5 does not exist SmugglerApi
        public void ImportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToImport = ItemType.Documents)
        {
            _logger.Information("The file to import = {0}", databaseName);

            var fileName = Path.ChangeExtension(databaseName, _ravenDumpExtension);

            var smugglerApi = new SmugglerDatabaseApi(new SmugglerDatabaseOptions
            {
                OperateOnTypes = itemTypeToImport,
                Incremental = false
            });

            var importOptions = new SmugglerImportOptions<RavenConnectionStringOptions>
            {
                FromFile = fileName,
                To = new RavenConnectionStringOptions
                {
                    DefaultDatabase = databaseName,
                    Url = _store.Url
                }
            };

            smugglerApi.ImportData(importOptions, null).Wait();
        }

        private int StartSmugglerProcess(string smugglerPath, string smugglerArgs)
        {
            _logger.Information("Smuggler Path = {0}", smugglerPath);
            _logger.Information("Smuggler Args = {0}", smugglerArgs);

            using (var proc = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    FileName = smugglerPath,
                    Arguments = smugglerArgs
                }
            })
            {
                proc.Start();

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var code = proc.ExitCode;

                _logger.Information($"Smuggler process output = {output}");

                return code;
            }
        }
    }
}
