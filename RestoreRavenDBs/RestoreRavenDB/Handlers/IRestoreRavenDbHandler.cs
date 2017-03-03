using System;

namespace RestoreRavenDB.Handlers
{
    public interface IRestoreRavenDbHandler
    {
        void SmugglerFullExport(Func<string, bool> conditionForDatabaseName = null);
        void SmugglerFullExport(string databaseName);
        void SmugglerFullImport(Func<string, bool> conditionForDatabaseName = null);
        void SmugglerFullImport(string databaseName);
        void CreateDatabase(string databaseName, params string[] additionalBundles);
        void DeleteDatabase(string databaseName);
    }
}
