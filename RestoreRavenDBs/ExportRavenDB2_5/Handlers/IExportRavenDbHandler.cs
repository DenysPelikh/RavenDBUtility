using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExportRavenDB2_5.Handlers
{
    internal interface IExportRavenDbHandler
    {
        void SmugglerFullExport(Func<string, bool> conditionForDatabaseName = null);
        void SmugglerFullExport(string databaseName);
    }
}
