using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Configuration;
using System.Security.Cryptography;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Client;
using RestoreRavenDBs.Extensions;
using Serilog;

namespace RestoreRavenDBs
{
    public class RestoreRavenDbHandler
    {
        private readonly IDocumentStore _store;
        private readonly ILogger _logger;

        private readonly string _backupDir;
        private readonly string _ravenDumpExtension;

        public RestoreRavenDbHandler(IDocumentStore store, ILogger logger)
        {
            _store = store;
            _logger = logger;

            _backupDir = ConfigurationManager.AppSettings["DefaultBackupDir"];
            _ravenDumpExtension = ".ravendump";
        }

        public RestoreRavenDbHandler(IDocumentStore store, ILogger logger, string backupDir) : this(store, logger)
        {
            _backupDir = backupDir;
        }

        public void SmugglerImport(string databaseName = null)
        {
            var searchFilePattern = "*" + _ravenDumpExtension;

            var files = string.IsNullOrWhiteSpace(databaseName)
                ? new[] { Path.ChangeExtension(databaseName, _ravenDumpExtension) }.OrderBy(x => x)
                : Directory.GetFiles(_backupDir, searchFilePattern, SearchOption.TopDirectoryOnly).OrderBy(x => x);

            foreach (var dbName in files.Select(Path.GetFileNameWithoutExtension))
            {
                DeleteDatabase(dbName);
            }

            _logger.Information("Done Deleting {0} database", files.Count());

            foreach (var fileName in files)
            {
                _logger.Information("The file to restore = {0}", fileName);

                var dbName = Path.GetFileNameWithoutExtension(fileName);

                CreateDatabase(dbName);

                var smugglerPath = AppDomain.CurrentDomain.BaseDirectory + @"Raven.Smuggler.exe";
                var smugglerArgs = @"in http://localhost:8080 " + fileName + " --database=" + dbName +
                                   " --negative-metadata-filter:@id=Raven/Encryption/Verification";

                try
                {
                    _logger.Information("smugglerPath = " + smugglerPath);
                    _logger.Information("smugglerArgs = " + smugglerArgs);
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

                        // To avoid deadlocks, always read the output stream first and then wait.
                        var output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        var code = proc.ExitCode;
                        _logger.Information($"smuggler output = {output}");
                        if (code != 0)
                        {
                            _logger.Warning($"smuggler failed the first time with this output {output}");
                            //try again
                            _logger.Warning($"sleeping for 15 seconds before trying again to backup {dbName}");
                            Thread.Sleep(TimeSpan.FromSeconds(15));
                            _logger.Information("trying to export again");
                            using (var proc2 = new Process
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

                                proc2.Start();
                                // To avoid deadlocks, always read the output stream first and then wait.
                                var secondoutput = proc2.StandardOutput.ReadToEnd();
                                proc2.WaitForExit();
                                var code2 = proc2.ExitCode;
                                if (code2 != 0)
                                    throw new Exception(
                                        $"Process {smugglerPath} didn't work with arguments {smugglerArgs}.  The output is {secondoutput}");

                                Thread.Sleep(TimeSpan.FromSeconds(10));
                                _logger.Information($"Succeeded the second time for {dbName}");
                            }
                        }
                        else
                            Thread.Sleep(TimeSpan.FromSeconds(10));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"An error occurred while trying to backup {dbName} with exception:{ex}");
                }
            }
        }

        //TODO: Comming soon
        //public void SmugglerExportViaSmugglerNativeProcess()
        //{
        //    var sysCommands = store.DatabaseCommands.ForSystemDatabase();


        //    var index = 0;
        //    var csDbList = new List<string>();

        //    var dbs = sysCommands.GetDatabaseNames(100, index);

        //    while (dbs.Any())
        //    {
        //        csDbList.AddRange(from dbName in dbs
        //                          where dbName.StartsWith("cs", StringComparison.OrdinalIgnoreCase)
        //                          let doc = sysCommands.Get("Raven/Databases/" + dbName)
        //                          let d = doc.DataAsJson
        //                          let disabled = d.Value<bool>("Disabled")
        //                          where !disabled
        //                          select dbName);

        //        index += dbs.Length;

        //        dbs = store.DatabaseCommands.ForSystemDatabase().GetDatabaseNames(100, index);
        //    }
        //    _logger.Information("Total dbs to backup = " + csDbList.Count);

        //    foreach (var dbName in csDbList)
        //    {
        //        _logger.Information("Backing up database " + dbName);

        //        var smugglerArgs = @"out http://localhost:8080/databases/" + dbName + @" C:\Backups-Raven\" + dbName +
        //                           ".raven";
        //        _logger.Information("smugglerArgs = " + smugglerArgs);
        //        var proc = Process.Start(AppDomain.CurrentDomain.BaseDirectory + @"Raven.Smuggler.exe", smugglerArgs);
        //        if (proc != null)
        //        {
        //            proc.WaitForExit();
        //            var code = proc.ExitCode;
        //            if (code != 0)
        //                Console.ReadLine();

        //            _logger.Information("exit code = " + code);
        //            Thread.Sleep(1000);
        //        }
        //        else
        //            _logger.Information("Process didn't work.");
        //    }
        //}

        //TODO: Comming soon
        public void ExportDatabaseViaSmugglerApi(string databaseName)
        {
        }

        //TODO: Comming soon
        //TODO: we use Console Process and Smuggler.exe 3.5 for this
        public void ImportDatabaseViaSmugglerNativeProcess(string databaseName)
        {
            _logger.Information("The file to restore = {0}", databaseName);

            var fileName = Path.ChangeExtension(databaseName, _ravenDumpExtension);
            var dbName = Path.GetFileNameWithoutExtension(databaseName);

            CreateDatabase(dbName);

            //TODO: need to change this on parameters
            var smugglerPath = AppDomain.CurrentDomain.BaseDirectory + @"Raven.Smuggler.exe";
            var smugglerArgs = @"in http://localhost:8080 " + fileName + " --database=" + dbName +
                               " --negative-metadata-filter:@id=Raven/Encryption/Verification";

            try
            {
                _logger.Information("smugglerPath = {0}", smugglerPath);
                _logger.Information("smugglerArgs = {0}", smugglerArgs);
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

                    // To avoid deadlocks, always read the output stream first and then wait.
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    var code = proc.ExitCode;
                    _logger.Information($"smuggler output = {output}");
                    if (code != 0)
                    {
                        _logger.Warning($"smuggler failed the first time with this output {output}");
                        //try again
                        _logger.Warning($"sleeping for 15 seconds before trying again to backup {dbName}");
                        Thread.Sleep(TimeSpan.FromSeconds(15));
                        _logger.Information("trying to export again");
                        using (var proc2 = new Process
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

                            proc2.Start();
                            // To avoid deadlocks, always read the output stream first and then wait.
                            var secondoutput = proc2.StandardOutput.ReadToEnd();
                            proc2.WaitForExit();
                            var code2 = proc2.ExitCode;
                            if (code2 != 0)
                                throw new Exception(
                                    $"Process {smugglerPath} didn't work with arguments {smugglerArgs}.  The output is {secondoutput}");

                            Thread.Sleep(TimeSpan.FromSeconds(10));
                            _logger.Information($"Succeeded the second time for {dbName}");
                        }
                    }
                    else
                        Thread.Sleep(TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error occurred while trying to backup {dbName} with exception:{ex}");
            }
        }

        //TODO: Comming soon
        //TODO: current version RavenDb Client 3.5, we use Smuggler.exe 3.0 because Smuggler.exe 3.5 does not exist SmugglerApi
        public void ImportDatabaseViaSmugglerApi(string databaseName)
        {
            //_logger.Information("The file to import = {0}", databaseName);

            //var dbName = Path.GetFileNameWithoutExtension(databaseName);

            //CreateDatabase(dbName);

            //var smugglerPath = AppDomain.CurrentDomain.BaseDirectory + @"Raven.Smuggler.3.5.exe";
            //var smugglerArgs = @"in http://localhost:8080 " + fileName + " --database=" + dbName +
            //                   " --negative-metadata-filter:@id=Raven/Encryption/Verification";

            //try
            //{
            //    var smugglerApi = new SmugglerDatabaseApi(new SmugglerDatabaseOptions
            //    {
            //        OperateOnTypes = ItemType.Documents | ItemType.Indexes | ItemType.Transformers,
            //        Incremental = false,
            //    });

            //    var exportOptions = new SmugglerExportOptions<RavenConnectionStringOptions>
            //    {
            //        ToFile = backupName,
            //        From = new RavenConnectionStringOptions
            //        {
            //            DefaultDatabase = dbName,
            //            Url = _store.Url,
            //        }
            //    };

            //    await smugglerApi.ExportData(exportOptions);

            //}
            //catch (Exception ex)
            //{
            //    _logger.Error(ex, $"An error occurred while trying to backup {dbName} with exception: {ex}");
            //}
        }

        public void CreateDatabase(string databaseName, params string[] bundles)
        {
            var dbName = Path.GetFileNameWithoutExtension(databaseName);

            if (_store.DatabaseExists(dbName)) return;

            string[] defaultBundles = { "Encryption", "Compression" };
            var key = GenerateKey();

            _store.DatabaseCommands.GlobalAdmin.CreateDatabase(new DatabaseDocument
            {
                Id = dbName,
                Settings =
                {
                    {"Raven/DataDir", "~\\" + dbName},
                    {"Raven/ActiveBundles", string.Join(";", defaultBundles.Union(bundles))},
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
            var dbName = Path.GetFileNameWithoutExtension(databaseName);

            if (!_store.DatabaseExists(dbName)) return;

            _logger.Information("Deleting database = {0}", dbName);

            _store.DatabaseCommands.GlobalAdmin.DeleteDatabase(dbName, hardDelete: true);

            _logger.Information("Deletion complete");
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
