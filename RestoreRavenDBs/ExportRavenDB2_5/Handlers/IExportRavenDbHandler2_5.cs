using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExportRavenDB2_5.Handlers
{
    internal interface IExportRavenDbHandler2_5
    {
        void SmugglerFullExport(Func<string, bool> conditionForDatabaseName = null);
        void SmugglerFullExport(string databaseName);
    }
}
