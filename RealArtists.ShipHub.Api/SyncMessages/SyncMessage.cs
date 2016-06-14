namespace RealArtists.ShipHub.Api.SyncMessages {
  using System.Collections.Generic;
  using System.Runtime.Serialization;

  public enum SyncLogAction {
    [EnumMember(Value = "oops")]
    Unspecified = 0, // Catch uninitialized values

    [EnumMember(Value = "set")]
    Set,

    [EnumMember(Value = "delete")]
    Delete
  }

  public enum SyncEntityType {
    [EnumMember(Value = "oops")]
    Unspecified = 0, // Catch uninitialized values

    [EnumMember(Value = "account")]
    Account,

    [EnumMember(Value = "comment")]
    Comment,

    [EnumMember(Value = "event")]
    Event,

    [EnumMember(Value = "issue")]
    Issue,

    [EnumMember(Value = "milestone")]
    Milestone,

    [EnumMember(Value = "repository")]
    Repository,
  }

  public abstract class SyncEntity {
    // Empty
  }

  public class SyncLogEntry {
    public SyncLogAction Action { get; set; }
    public SyncEntityType Entity { get; set; }
    public SyncEntity Data { get; set; }
  }

  public class SyncMessage : SyncMessageBase {
    public SyncMessage() {
      MessageType = "sync";
    }

    public IEnumerable<SyncLogEntry> Logs { get; set; }
    public VersionDetails Version { get; set; }
    public long Remaining { get; set; }
  }
}