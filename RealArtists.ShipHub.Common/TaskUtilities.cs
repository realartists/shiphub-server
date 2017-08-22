namespace RealArtists.ShipHub.Common {
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System.Threading.Tasks;

  public static class TaskUtilities {
    /// <summary>
    /// Observes and logs a potential exception on a given Task.
    /// If a Task fails and throws an exception which is never observed, it will be caught by the .NET finalizer thread.
    /// This function awaits the given task and if the exception is thrown, it observes this exception and logs it.
    /// This will prevent the escalation of this exception to the .NET finalizer thread.
    /// </summary>
    /// <param name="task">The task to be logged.</param>
    public static void LogFailure(this Task task, string userInfo = null, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      if (task.IsCompleted) {
        var e = task.Exception;
        if (e != null) {
          task.Exception.Report(userInfo: userInfo, filePath: filePath, memberName: memberName, lineNumber: lineNumber);
        }
      } else {
        task.ContinueWith(
          t => {
            t.Exception.Report(userInfo: userInfo, filePath: filePath, memberName: memberName, lineNumber: lineNumber);
          },
          CancellationToken.None,
          TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      }
    }
  }
}
