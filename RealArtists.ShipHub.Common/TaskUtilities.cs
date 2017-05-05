namespace RealArtists.ShipHub.Common {
  using System;
  using System.Runtime.CompilerServices;
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
        if (task.IsFaulted) {
          LogIt(task);
        }
      } else {
        task.ContinueWith(
          t => {
            LogIt(task);
          },
          TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
      }

      void LogIt(Task completedTask)
      {
        try {
          // This is safe because we know the task has completed and cannot deadlock in the scheduler.
          completedTask.GetAwaiter().GetResult();
        } catch (Exception e) {
          e.Report(userInfo: userInfo, filePath: filePath, memberName: memberName, lineNumber: lineNumber);
        }
      }
    }
  }
}
