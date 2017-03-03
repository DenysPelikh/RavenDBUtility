using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Abstractions.Smuggler;
using Raven.Client;
using Raven.Smuggler;
using Serilog;

namespace RestoreRavenDB.Common
{
    public class SmugglerWrapper : ISmugglerWrapper
    {
        private readonly IDocumentStore _store;
        private readonly ILogger _logger;

        private readonly double _breakTimeSeconds;

        private string _backupDir;
        public string BackupDir
        {
            get
            {
                return _backupDir;
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (value != string.Empty && !Directory.Exists(value))
                {
                    Directory.CreateDirectory(value);
                }

                _backupDir = value;
            }
        }

        public SmugglerWrapper(IDocumentStore store, ILogger logger)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _store = store;
            _logger = logger;

            _breakTimeSeconds = 5;
            BackupDir = string.Empty; //From current Dir
        }

        //We use Console Process and Smuggler.exe 3.5 for this
        public void ExportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.Warning("Database name incorrectly");
                return;
            }

            _logger.Information("Export database {0} with process", databaseName);

            var filePath = GetFilePathFromDatabaseName(databaseName);

            var actionPath = $"out {_store.Url} ";
            var smugglerOptionArguments = $" {string.Join(" ", additionalSmugglerArguments)}";

            var smugglerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Raven.Smuggler.3.5.exe");
            var smugglerArgs = string.Concat(actionPath, filePath, " --database=", databaseName, smugglerOptionArguments);

            try
            {
                //TODO probably need to add this event when exitCode != 0 or something and also consider other way
                var exitCode = StartSmugglerProcess(smugglerPath, smugglerArgs);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error occurred while trying to export {databaseName} with exception: {ex}");
            }

            Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));
        }

        public void ExportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToExport = ItemType.Documents)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.Warning("Database name incorrectly");
                return;
            }

            _logger.Information("Export database {0} with Smuggler Api", databaseName);

            var filePath = GetFilePathFromDatabaseName(databaseName);

            var smugglerApi = new SmugglerDatabaseApi(new SmugglerDatabaseOptions
            {
                OperateOnTypes = itemTypeToExport,
                Incremental = false
            });

            var exportOptions = new SmugglerExportOptions<RavenConnectionStringOptions>
            {
                ToFile = filePath,
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
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.Warning("Database name incorrectly");
                return;
            }

            _logger.Information("Import database {0} with process", databaseName);

            var filePath = GetFilePathFromDatabaseName(databaseName);

            var actionPath = $"in {_store.Url} ";
            var smugglerOptionArguments = $" --negative-metadata-filter:@id=Raven/Encryption/Verification {string.Join(" ", additionalSmugglerArguments)}";

            var smugglerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Raven.Smuggler.3.5.exe");
            var smugglerArgs = string.Concat(actionPath, filePath, " --database=", databaseName, smugglerOptionArguments);

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
                        //TODO probably need to add this event or something, consider optional Exception
                        throw new Exception($"Process {smugglerPath} didn't work with arguments {smugglerArgs}");
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));
                    _logger.Information("Succeeded the second time for {0}", databaseName);
                }
                else
                {
                    Thread.Sleep(TimeSpan.FromSeconds(_breakTimeSeconds));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error occurred while trying to backup {databaseName} with exception: {ex}");
            }
        }

        public void ImportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToImport = ItemType.Documents)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.Warning("Database name incorrectly");
                return;
            }

            _logger.Information("Import database {0} with Smuggler Api", databaseName);

            var filePath = GetFilePathFromDatabaseName(databaseName);

            var smugglerApi = new SmugglerDatabaseApi(new SmugglerDatabaseOptions
            {
                OperateOnTypes = itemTypeToImport,
                Incremental = false
            });

            var importOptions = new SmugglerImportOptions<RavenConnectionStringOptions>
            {
                FromFile = filePath,
                To = new RavenConnectionStringOptions
                {
                    DefaultDatabase = databaseName,
                    Url = _store.Url
                }
            };

            smugglerApi.ImportData(importOptions, null).Wait();
        }

        private string GetFilePathFromDatabaseName(string databaseName)
        {
            var filePath = Path.Combine(BackupDir, databaseName);

            return filePath;
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

                if (code != 0)
                {
                    _logger.Warning($"Process {smugglerPath} didn't work with arguments {smugglerArgs}");
                    _logger.Warning($"Smuggler process output = {output}");
                }
                else
                {
                    _logger.Information($"Smuggler process output = {output}");
                }

                return code;
            }
        }
    }
}
