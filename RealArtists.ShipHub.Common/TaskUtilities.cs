namespace RealArtists.ShipHub.Common {
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
    public static void LogFailure(this Task task, string userInfo = null) {
      if (task.IsCompleted) {
        if (task.IsFaulted) {
          task.Exception.Report(userInfo: userInfo);
        }
      } else {
        task.ContinueWith(
          t => {
            if (t.IsFaulted) {
              t.Exception.Report(userInfo: userInfo);
            }
          },
          CancellationToken.None,
          TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      }
    }
  }
}
