using Raven.Abstractions.Smuggler;

namespace RestoreRavenDB.Common
{
    public interface ISmugglerWrapper
    {
        string BackupDir { get; set; }
        void ExportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments);
        void ExportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToExport = ItemType.Documents);
        bool ImportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments);
        void ImportDatabaseSmugglerApi(string databaseName, ItemType itemTypeToImport = ItemType.Documents);
    }
}
