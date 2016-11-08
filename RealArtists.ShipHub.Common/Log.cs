using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Azure;

namespace RealArtists.ShipHub.Common {
  public sealed class Log {
    private Log() { } // don't instantiate me

    /// <summary>
    /// Logs filePath, memberName, and lineNumber to the log
    /// </summary>
    public static void Trace([CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      var line = new LogLine() {
        FilePath = filePath,
        LineNumber = lineNumber,
        Method = memberName,
        Level = LogLine.LogLevel.Trace
      };
      Sink.WriteLine(line);
#if DEBUG
      WriteLineToConsole(line);
#endif
    }

    /// <summary>
    /// Logs message iff this is a DEBUG build.
    /// </summary>
    public static void Debug(Func<string> messageFun, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
#if DEBUG
      var line = new LogLine() {
        FilePath = filePath,
        LineNumber = lineNumber,
        Method = memberName,
        Level = LogLine.LogLevel.Debug,
        Message = messageFun()
      };
      Sink.WriteLine(line);
      WriteLineToConsole(line);
#endif
    }

    /// <summary>
    /// Logs msg unconditional at Info level
    /// </summary>
    public static void Info(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      var line = new LogLine() {
        FilePath = filePath,
        LineNumber = lineNumber,
        Method = memberName,
        Level = LogLine.LogLevel.Info,
        Message = message
      };
      Sink.WriteLine(line);
#if DEBUG
      WriteLineToConsole(line);
#endif
    }

    /// <summary>
    /// Logs msg unconditionally at Error level
    /// </summary>
    public static void Error(string message, [CallerFilePath] string filePath = "", [CallerMemberName] string memberName = "", [CallerLineNumber] int lineNumber = 0) {
      var line = new LogLine() {
        FilePath = filePath,
        LineNumber = lineNumber,
        Method = memberName,
        Level = LogLine.LogLevel.Error,
        Message = message
      };
      Sink.WriteLine(line);
#if DEBUG
      WriteLineToConsole(line);
#endif
    }

    /// <summary>
    /// Logs exception unconditionally at Critical level.
    /// </summary>
    public static void Exception(Exception ex, string message = null) {
      var e = ex.Simplify();
      string m = "";
      if (message != null && e.Message != null) {
        m = $"{message} - {e.Message}";
      } else if (message != null) {
        m = message;
      } else if (e.Message != null) {
        m = e.Message;
      }
      var line = new LogLine() {
        Component = e.TargetSite?.DeclaringType.AssemblyQualifiedName,
        Level = LogLine.LogLevel.Exception,
        Message = m,
        StackTrace = e.StackTrace
      };
      Sink.WriteLine(line);
#if DEBUG
      WriteLineToConsole(line);
#endif
    }

    /* Implementation */

    private static Lazy<LogSink> _sink = new Lazy<LogSink>(() => new LogSink());
    private static LogSink Sink {
      get { return _sink.Value; }
    }

#if DEBUG
    private static void WriteLineToConsole(LogLine line) {
      Console.WriteLine(line.Message);
    }
#endif

    private class LogLine {
      public enum LogLevel {
        Trace,
        Debug,
        Info,
        Error,
        Exception
      };

      public LogLine() {
        Timestamp = DateTimeOffset.Now;
      }
      public DateTimeOffset Timestamp { get; set; }
      public string Sender {
        get {
          return _sender.Value;
        }
      }
      public string HostName {
        get {
          return _hostName.Value;
        }
      }

      public string Component { get; set; }
      public string Message { get; set; }
      public string FilePath { get; set; }
      public string Method { get; set; }
      public int LineNumber { get; set; }
      public LogLevel Level { get; set; }
      public string StackTrace { get; set; }

      public string Formatted {
        get {
          if (Level == LogLevel.Trace) {
            return $"[{Level.ToString().ToUpperInvariant()}] - {HostName} - {FilePath}:{LineNumber} {Method}";
          } else if (Level == LogLevel.Exception) {
            return $"[{Level.ToString().ToUpperInvariant()}] - {HostName} - {Component} - {Message}\n{StackTrace}";
          } else {
            return $"[{Level.ToString().ToUpperInvariant()}] - {HostName} - {FilePath}:{LineNumber} {Method} - {Message}";
          }
        }
      }

      private static Lazy<string> _sender = new Lazy<string>(() => {
        return CloudConfigurationManager.GetSetting("DeploymentId");
      });
      
      private static Lazy<string> _hostName = new Lazy<string>(() => {
        return Dns.GetHostName();
      });
    }

    class LogSink : IDisposable {

      private const long HysteresisMillis = 2000; // time we wait to flush when logs are sent slowly
      private const long FlushCount = 1000; // logs will be flushed immediately if more than this amount queue up
      private const long MaxBufferCount = 10000; // max logs to buffer. above this count, we start dropping them.

      private Syslog _syslog;
      private List<LogLine> _buffer = new List<LogLine>();
      private Timer _timer;
      private bool _timerScheduled = false;
      private Thread _consumerThread;
      
      public LogSink() {
        _syslog = new Syslog() {
          Host = "logs.papertrailapp.com",
          Port = 36114
        };
        _timer = new Timer((ctx) => {
          ((LogSink)ctx).TimerFired();
        }, this, Timeout.Infinite, Timeout.Infinite);
        _consumerThread = new Thread(ProcessBuffer);
        _consumerThread.Start();
      }

      public void Dispose() {
        _timer.Dispose();
      }

      /* Add a single line to _buffer. Callable from any thread */
      public void WriteLine(LogLine line) {
        lock (_buffer) {
          if (_buffer.Count >= MaxBufferCount) {
            return;
          }
          _buffer.Add(line);
          if (_buffer.Count >= FlushCount) {
            if (_timerScheduled) {
              _timerScheduled = false;
              _timer.Change(Timeout.Infinite, Timeout.Infinite); // cancel the timer
            }
            Monitor.Pulse(_buffer);
          } else if (!_timerScheduled) {
            _timerScheduled = true;
            _timer.Change(HysteresisMillis, Timeout.Infinite);
          } // else timer will get it later
        }
      }

      private void TimerFired() {
        lock (_buffer) {
          _timerScheduled = false;
          Monitor.Pulse(_buffer);
        }
      }

      // Runs on a dedicated thread, serially processing buffer
      private void ProcessBuffer() {
        do {
          LogLine[] lines = null;
          lock (_buffer) {
            if (_buffer.Count == 0) {
              Monitor.Wait(_buffer);
              if (_buffer.Count == 0)
                continue; // skip spurious wakeup
            }

            lines = new LogLine[_buffer.Count];
            _buffer.CopyTo(lines);
            _buffer.Clear();
          }

          WriteLines(lines);

        } while (true);
      }

      [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
      private void WriteLines(LogLine[] lines) {
        try {
          _syslog.WriteLines(lines);
        } catch (Exception e) {
          Console.Error.WriteLine(e.ToString());
        }
      }
    }


    private class Syslog {
      public Syslog() { }
      public string Host { get; set; }
      public int Port { get; set; }

      private int LevelToSeverity(LogLine.LogLevel level) {
        switch (level) {
          case LogLine.LogLevel.Info:
          case LogLine.LogLevel.Trace: return 6; // Informational
          case LogLine.LogLevel.Debug: return 7; // Debug
          case LogLine.LogLevel.Error: return 3; // Error
          case LogLine.LogLevel.Exception: return 2; // Critical
        }
        return 1;
      }

      private static Lazy<string> _progName = new Lazy<string>(() => {
        return AppDomain.CurrentDomain.FriendlyName;
      });
      private byte[] LogLineBytes(LogLine[] lines) {
        var bytes = new List<byte>();
        var progName = _progName.Value;
        foreach (var line in lines) {
          var formatted = $"<22>{LevelToSeverity(line.Level)} {line.Timestamp.ToString("o")} {line.Sender} {progName} - - - {line.Formatted}\n";
          bytes.AddRange(Encoding.UTF8.GetBytes(formatted));
        }
        return bytes.ToArray();
      }

      public void WriteLines(LogLine[] lines) {
        using (var tcp = new TcpClient(Host, Port)) {
          var stream = tcp.GetStream();
          using (var ssl = new SslStream(stream, true)) {
            ssl.AuthenticateAsClient(Host);
            ssl.Write(LogLineBytes(lines));
          }
        }
      }
    }
  }
}
