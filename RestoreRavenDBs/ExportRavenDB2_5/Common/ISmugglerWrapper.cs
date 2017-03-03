using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExportRavenDB2_5.Common
{
    public interface ISmugglerWrapper
    {
        string BackupDir { get; set; }
        void ExportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments);
        void ImportDatabaseNativeProcess(string databaseName, params string[] additionalSmugglerArguments);
    }
}
