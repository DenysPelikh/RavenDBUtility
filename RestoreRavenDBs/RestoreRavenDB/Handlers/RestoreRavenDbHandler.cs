using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Raven.Abstractions.Data;
using Raven.Abstractions.Smuggler;
using Raven.Client;
using RestoreRavenDB.Common;
using RestoreRavenDB.Extensions;
using Serilog;

namespace RestoreRavenDB.Handlers
{
    public class RestoreRavenDbHandler : IRestoreRavenDbHandler
    {
        private readonly IDocumentStore _store;
        private readonly ILogger _logger;
        private readonly ISmugglerWrapper _smugglerWrapper;

        private readonly string _backupDir;
        private readonly string _ravenDumpExtension;
        private bool _useSmugglerApi = true;

        public RestoreRavenDbHandler(IDocumentStore store, ILogger logger, ISmugglerWrapper smugglerWrapper, string backupDir = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (smugglerWrapper == null) throw new ArgumentNullException(nameof(smugglerWrapper));

            _store = store;
            _logger = logger;
            _smugglerWrapper = smugglerWrapper;

            _backupDir = backupDir ?? string.Empty; //From current Dir
            _ravenDumpExtension = ".ravendump";

            smugglerWrapper.BackupDir = _backupDir;
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

            foreach (var databaseName in filteredDatabaseNames)
            {
                _smugglerWrapper.ExportDatabaseNativeProcess(databaseName, "--operate-on-types=Documents");
            }
        }

        public void SmugglerFullExport(string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.Warning("Database name incorrectly");
                return;
            }

            _smugglerWrapper.ExportDatabaseNativeProcess(databaseName, "--operate-on-types=Documents");
        }

        public void SmugglerFullImport(Func<string, bool> conditionForDatabaseName = null)
        {
            var searchFilePattern = "*" + _ravenDumpExtension;

            var files = Directory.GetFiles(_backupDir, searchFilePattern, SearchOption.TopDirectoryOnly);

            if (conditionForDatabaseName != null)
                files = files.Where(conditionForDatabaseName).ToArray();

            var databaseNamesInOrder = files.Select(Path.GetFileNameWithoutExtension).OrderBy(x => x);

            foreach (var databaseName in databaseNamesInOrder)
            {
                DeleteDatabase(databaseName);
            }

            _logger.Information("Done Deleting {0} database", databaseNamesInOrder.Count());

            foreach (var databaseName in databaseNamesInOrder)
            {
                _logger.Information("The database to restore = {0}", databaseName);

                ImportDatabase(databaseName);
            }
        }

        public void SmugglerFullImport(string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                _logger.Warning("Database name incorrectly");
                return;
            }

            DeleteDatabase(databaseName);

            ImportDatabase(databaseName);
        }

        private void ImportDatabase(string databaseName)
        {
            CreateDatabase(databaseName, GetAdditionalBundles(databaseName));

            if (_useSmugglerApi)
            {
                // I'm importing the indexes first, as it's quicker to reset them this way, and bypasses any
                if (_smugglerWrapper.ImportDatabaseSmugglerApi(databaseName, ItemType.Indexes | ItemType.Transformers | ItemType.Attachments | ItemType.RemoveAnalyzers))
                {
                    ResetIndexes(databaseName);

                    if (_smugglerWrapper.ImportDatabaseSmugglerApi(databaseName)) // Import Documents only
                    {
                        ActivateBundles(databaseName);
                    }
                }
            }
            else
            {
                var importArgs = GetImportArgs(databaseName);
                if (_smugglerWrapper.ImportDatabaseNativeProcess(databaseName, importArgs))
                {
                    ResetIndexes(databaseName);
                    ActivateBundles(databaseName);
                }
            }
        }
        public void CreateDatabase(string databaseName, params string[] additionalBundles)
        {
            if (_store.DatabaseExists(databaseName))
            {
                _logger.Warning("Database {0} already exists", databaseName);
                return;
            }

            string[] defaultBundles = { "Encryption", "Compression" };
            var key = GenerateKey();

            _store.DatabaseCommands.GlobalAdmin.CreateDatabase(new DatabaseDocument
            {
                Id = databaseName,
                Settings =
                {
                    {"Raven/DataDir", "~\\" + databaseName},
                    {"Raven/ActiveBundles", string.Join(";", defaultBundles.Union(additionalBundles))}
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
            if (!_store.DatabaseExists(databaseName))
            {
                _logger.Warning("Database {0} does not exist", databaseName);
                return;
            }

            _logger.Information("Deleting database = {0}", databaseName);

            _store.DatabaseCommands.GlobalAdmin.DeleteDatabase(databaseName, hardDelete: true);

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

        private static string[] GetAdditionalBundles(string databaseName)
        {
            var additionalBundles = new List<string>();
            if (databaseName.StartsWith("cs.RA", StringComparison.OrdinalIgnoreCase))
            {
                additionalBundles.Add("Versioning");
            }

            return additionalBundles.ToArray();
        }

        private void ActivateBundles(string databaseName)
        {
            if (databaseName.StartsWith("cs.RA", StringComparison.OrdinalIgnoreCase))
            {
                ActivateBundle("Unique Constraints", databaseName);
            }
        }

        /// <summary>
        /// Ensure a bundle is activated
        /// </summary>
        /// <param name="bundleName"></param>
        /// <param name="databaseName"></param>
        public void ActivateBundle(string bundleName, string databaseName)
        {
            _logger.Information("Activating {0} bundle on {1}", bundleName, databaseName);
            try
            {
                using (var session = _store.OpenSession())
                {
                    var databaseDocument = session.Load<DatabaseDocument>("Raven/Databases/" + databaseName);

                    session.Advanced.GetMetadataFor(databaseDocument)[Constants.AllowBundlesChange] = true;

                    var settings = databaseDocument.Settings;
                    var activeBundles = settings.ContainsKey(Constants.ActiveBundles) ? settings[Constants.ActiveBundles] : null;
                    if (string.IsNullOrEmpty(activeBundles))
                        settings[Constants.ActiveBundles] = bundleName;
                    else if (!activeBundles.Split(new char[] { ';' }).Contains(bundleName, StringComparer.OrdinalIgnoreCase))
                        settings[Constants.ActiveBundles] = activeBundles + ";" + bundleName;
                    session.SaveChanges();
                }

                _logger.Information("Bundle Activated!");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"An error occurred while trying to activate bundle: {ex}");
            }
        }

        private string[] GetImportArgs(string databaseName)
        {
            return new[] { "--disable-versioning-during-import=true" };
        }

        private void ResetIndexes(string databaseName)
        {
            _logger.Information("Resetting indexes");
            var indexes = _store.DatabaseCommands.ForDatabase(databaseName).GetIndexes(0, 100);

            foreach (var index in indexes)
            {
                try
                {
                    if (!index.IsCompiled)
                    {
                        _logger.Information("Resetting index: {0}", index.Name);
                        _store.DatabaseCommands.ForDatabase(databaseName).GlobalAdmin.Commands.ResetIndex(index.Name);
                        _logger.Information("Reset successful.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Unable to reset index {0}: {1}", index.Name, ex);
                }

            }
        }
    }
}
