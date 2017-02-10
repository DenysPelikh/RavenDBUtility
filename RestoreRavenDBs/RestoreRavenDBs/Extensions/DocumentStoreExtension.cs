using Raven.Client;

namespace RestoreRavenDBs.Extensions
{
    public static class DocumentStoreExtension
    {
        public static bool DatabaseExists(this IDocumentStore store, string databaseName)
        {
            var headers = store.DatabaseCommands.ForSystemDatabase().Head("Raven/Databases/" + databaseName);
            return headers != null;
        }
    }
}
