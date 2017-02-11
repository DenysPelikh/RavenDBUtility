using Raven.Abstractions.Smuggler;

namespace RestoreRavenDBs.Common
{
    public interface ISmugglerWrapper
    {
        void ExportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments);
        void ExportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToExport = ItemType.Documents);
        void ImportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments);
        void ImportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToImport = ItemType.Documents);
    }
}
