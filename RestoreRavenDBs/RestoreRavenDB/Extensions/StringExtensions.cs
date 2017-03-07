using System.IO;

namespace RestoreRavenDB.Extensions
{
    public static class StringExtensions
    {
        public static void EnsureFileDestination(this string path)
        {
            if (path != string.Empty && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
