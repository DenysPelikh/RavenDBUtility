using System;
using System.Collections.Generic;
using System.Linq;
using ExportRavenDB2_5.Common;
using Raven.Client;
using Serilog;

namespace ExportRavenDB2_5.Handlers
{
    public class ExportRavenDbHandler : IExportRavenDbHandler
    {
        private readonly IDocumentStore _store;
        private readonly ILogger _logger;
        private readonly ISmugglerWrapper _smugglerWrapper;

        public ExportRavenDbHandler(IDocumentStore store, ILogger logger, ISmugglerWrapper smugglerWrapper, string backupDir = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (smugglerWrapper == null) throw new ArgumentNullException(nameof(smugglerWrapper));

            _store = store;
            _logger = logger;
            _smugglerWrapper = smugglerWrapper;

            var backupDir1 = backupDir ?? string.Empty;
            

            smugglerWrapper.BackupDir = backupDir1;
        }

        public void SmugglerFullExport(Func<string, bool> conditionForDatabaseName = null)
        {
            var sysCommands = _store.DatabaseCommands.ForSystemDatabase();

            var index = 0;
            var filteredDatabaseNames = new List<string>();

            var databaseNames = _store.DatabaseCommands.GetDatabaseNames(100, index);

            while (databaseNames.Any())
            {
                filteredDatabaseNames.AddRange(from dbName in databaseNames
                                               where conditionForDatabaseName == null || conditionForDatabaseName(dbName)
                                               let doc = sysCommands.Get("Raven/Databases/" + dbName)
                                               let d = doc.DataAsJson
                                               let disabled = d.Value<bool>("Disabled")
                                               where !disabled
                                               select dbName);

                index += databaseNames.Length;

                databaseNames = _store.DatabaseCommands.GetDatabaseNames(100, index);
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
    }
}
