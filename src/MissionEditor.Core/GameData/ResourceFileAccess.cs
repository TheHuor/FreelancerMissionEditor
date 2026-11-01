using System.Collections.Concurrent;

namespace MissionEditor.Core.Model;

// A writer waits for our outstanding resource reads before replacing a DLL.
// Concurrent targeted reads and background enumeration can share a read lock.
internal static class ResourceFileAccess
{
  private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> Gates = new (StringComparer.OrdinalIgnoreCase);
  internal static ReaderWriterLockSlim Gate (string path) => Gates.GetOrAdd (Path.GetFullPath (path),
      _ => new ReaderWriterLockSlim (LockRecursionPolicy.SupportsRecursion));
}
