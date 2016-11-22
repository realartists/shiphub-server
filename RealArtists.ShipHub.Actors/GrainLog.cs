namespace RealArtists.ShipHub.Actors {
  using System;
  using System.Diagnostics.CodeAnalysis;
  using System.Runtime.CompilerServices;
  using Orleans;

  public static class GrainLog {
    /// <summary>
    /// Logs filePath, memberName, and lineNumber to the log
    /// </summary>
    [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "grain", Justification = "Mirror Common.Log API")]
    public static void Trace(this IGrain grain, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      // Would love to add grain info here. Maybe ask James.
      Common.Log.Trace(filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Logs message iff this is a DEBUG build.
    /// </summary>
    public static void Debug(this IGrain grain, Func<string> messageFun, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      Common.Log.Debug(() => $"{grain.LoggingIdentifier()}{messageFun()}", filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Logs msg unconditional at Info level
    /// </summary>
    public static void Info(this IGrain grain, string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      Common.Log.Info($"{grain.LoggingIdentifier()}{message}", filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Logs msg unconditionally at Error level
    /// </summary>
    public static void Error(this IGrain grain, string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      Common.Log.Error($"{grain.LoggingIdentifier()}{message}", filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Logs exception unconditionally at Critical level.
    /// </summary>
    public static void Exception(this IGrain grain, Exception ex, string message = null) {
      Common.Log.Exception(ex, $"{grain.LoggingIdentifier()}{message}");
    }

    public static string LoggingIdentifier(this IGrain grain) {
      var type = grain.GetType();

      string temp = null;
      if (typeof(IGrainWithIntegerKey).IsAssignableFrom(type)) {
        return $"[{grain.GetPrimaryKeyLong()}] ";
      } else if (typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(type)) {
        var key = grain.GetPrimaryKeyLong(out temp);
        return $"[{key} {temp}] ";
      } else if (typeof(IGrainWithGuidKey).IsAssignableFrom(type)) {
        return $"[{grain.GetPrimaryKey()}] ";
      } else if (typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(type)) {
        var key = grain.GetPrimaryKey(out temp);
        return $"[{key} {temp}] ";
      } else if (typeof(IGrainWithStringKey).IsAssignableFrom(type)) {
        return $"[{grain.GetPrimaryKeyString()}] ";
      }

      return string.Empty;
    }
  }
}
