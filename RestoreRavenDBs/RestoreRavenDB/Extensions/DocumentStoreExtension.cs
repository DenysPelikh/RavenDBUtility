using System;
using Raven.Client;

namespace RestoreRavenDB.Extensions
{
    public static class DocumentStoreExtension
    {
        public static bool DatabaseExists(this IDocumentStore store, string databaseName)
        {
            if (databaseName == null) throw new ArgumentNullException(nameof(databaseName));

            var headers = store.DatabaseCommands.ForSystemDatabase().Head("Raven/Databases/" + databaseName);
            return headers != null;
        }
    }
}
