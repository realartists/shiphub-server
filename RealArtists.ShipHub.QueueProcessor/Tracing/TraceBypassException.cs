namespace RealArtists.ShipHub.QueueProcessor.Tracing {
  using System;
  using System.Runtime.Serialization;
  using System.Security.Permissions;

  /// <summary>
  /// This exception wrapper is used to ensure web jobs still fail but aren't double-logged.
  /// </summary>
  [Serializable]
  public class TraceBypassException : Exception {
    public TraceBypassException() {
    }

    public TraceBypassException(string message) : base(message) {
    }

    public TraceBypassException(string message, Exception innerException) : base(message, innerException) {
    }

    protected TraceBypassException(SerializationInfo info, StreamingContext context) : base(info, context) {
    }

    [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
    public override void GetObjectData(SerializationInfo info, StreamingContext context) {
      base.GetObjectData(info, context);
    }
  }
}
