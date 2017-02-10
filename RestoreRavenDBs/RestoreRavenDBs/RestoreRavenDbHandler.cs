using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Configuration;
using System.Security.Cryptography;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Abstractions.Smuggler;
using Raven.Client;
using RestoreRavenDBs.Extensions;
using Serilog;
using Raven.Smuggler;

namespace RestoreRavenDBs
{
    public class RestoreRavenDbHandler
    {
        private readonly IDocumentStore _store;
        private readonly ILogger _logger;

        private readonly string _backupDir;
        private readonly string _ravenDumpExtension;
        private readonly double _breakTimeSeconds;

        public RestoreRavenDbHandler(IDocumentStore store, ILogger logger)
        {
            _store = store;
            _logger = logger;

            _backupDir = ConfigurationManager.AppSettings["DefaultBackupDir"];
            _ravenDumpExtension = ".ravendump";
            _breakTimeSeconds = 5;
        }

        public RestoreRavenDbHandler(IDocumentStore store, ILogger logger, string backupDir) : this(store, logger)
        {
            _backupDir = backupDir;
        }

        //TODO: need add condition like in Export
        public void SmugglerFullImport(string databaseName = null)
        {
            var searchFilePattern = "*" + _ravenDumpExtension;

            var files = string.IsNullOrWhiteSpace(databaseName)
                ? new[] { databaseName }
                : Directory.GetFiles(_backupDir, searchFilePattern, SearchOption.TopDirectoryOnly);

            var databaseNamesInOrder = files.Select(Path.GetFileNameWithoutExtension).OrderBy(x => x);

            foreach (var dbName in databaseNamesInOrder)
            {
                DeleteDatabase(dbName);
            }

            _logger.Information("Done Deleting {0} database", databaseNamesInOrder.Count());

            foreach (var dbName in databaseNamesInOrder)
            {
                _logger.Information("The database to restore = {0}", dbName);

                CreateDatabase(dbName);

                ImportDatabaseNativeProcess(dbName);
            }
        }

        //TODO: consider this method
        public void SmugglerFullExport(string databaseName)
        {
            ExportDatabaseNativeProcess(databaseName);
        }

        public void SmugglerFullExport(Func<string, bool> conditionForDatabaseName = null)
        {
            var sysCommands = _store.DatabaseCommands.ForSystemDatabase();

            var index = 0;
            var filteredDatabaseNames = new List<string>();

            var databaseNames = _store.DatabaseCommands.GlobalAdmin.GetDatabaseNames(100, index);

            while (databaseNames.Any())
            {
                filteredDatabaseNames.AddRange(from dbName in databaseNames
                                               where conditionForDatabaseName != null && conditionForDatabaseName(dbName)
                                               let doc = sysCommands.Get("Raven/Databases/" + dbName)
                                               let d = doc.DataAsJson
                                               let disabled = d.Value<bool>("Disabled")
                                               where !disabled
                                               select dbName);

                index += databaseNames.Length;

                databaseNames = _store.DatabaseCommands.GlobalAdmin.GetDatabaseNames(100, index);
            }

            _logger.Information("Total databases to backup = " + filteredDatabaseNames.Count);

            foreach (var dbName in filteredDatabaseNames)
            {
                ExportDatabaseNativeProcess(dbName);
            }
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

            CreateDatabase(databaseName);

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
        public void ImportDatabaseViaSmugglerApi(string databaseName, ItemType itemTypeToImport = ItemType.Documents)
        {
            _logger.Information("The file to import = {0}", databaseName);

            CreateDatabase(databaseName);

            var fileName = Path.ChangeExtension(databaseName, _ravenDumpExtension);

            CreateDatabase(databaseName);


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

        public void CreateDatabase(string databaseName, params string[] additionalBundles)
        {
            if (_store.DatabaseExists(databaseName)) return;

            string[] defaultBundles = { "Encryption", "Compression" };
            var key = GenerateKey();

            _store.DatabaseCommands.GlobalAdmin.CreateDatabase(new DatabaseDocument
            {
                Id = databaseName,
                Settings =
                {
                    {"Raven/DataDir", "~\\" + databaseName},
                    {"Raven/ActiveBundles", string.Join(";", defaultBundles.Union(additionalBundles))},
                },
                SecuredSettings =
                {
                    {"Raven/Encryption/Key", key},
                    {
                        "Raven/Encryption/Algorithm", "System.Security.Cryptography.RijndaelManaged, mscorlib"
                    },
                    {"Raven/Encryption/KeyBitsPreference", "256"},
                    {"Raven/Encryption/EncryptIndexes", "True"}
                }
            });
        }

        public void DeleteDatabase(string databaseName)
        {
            if (!_store.DatabaseExists(databaseName)) return;

            _logger.Information("Deleting database = {0}", databaseName);

            _store.DatabaseCommands.GlobalAdmin.DeleteDatabase(databaseName, hardDelete: true);

            _logger.Information("Deletion complete");
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

        private static string GenerateKey()
        {
            using (var crypt = Rijndael.Create())
            {
                crypt.GenerateKey();
                return Convert.ToBase64String(crypt.Key);
            }
        }
    }
}
