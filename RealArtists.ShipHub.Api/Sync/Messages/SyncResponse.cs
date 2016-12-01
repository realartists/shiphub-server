namespace RealArtists.ShipHub.Api.Sync.Messages {
  using System;
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

    [EnumMember(Value = "comment")]
    Comment,

    [EnumMember(Value = "event")]
    Event,

    [EnumMember(Value = "issue")]
    Issue,

    [EnumMember(Value = "milestone")]
    Milestone,

    [EnumMember(Value = "org")]
    Organization,

    [EnumMember(Value = "repo")]
    Repository,

    [EnumMember(Value = "user")]
    User,

    [EnumMember(Value = "reaction")]
    Reaction,

    [EnumMember(Value = "label")]
    Label,

    [EnumMember(Value = "project")]
    Project,
  }

  public abstract class SyncEntity {
    // Empty
  }

  public class SyncLogEntry {
    private SyncLogAction _action;
    public SyncLogAction Action {
      get { return _action; }
      set {
        ThrowIfInvalid(value, _entity);
        _action = value;
      }
    }

    private SyncEntityType _entity;
    public SyncEntityType Entity {
      get { return _entity; }
      set {
        ThrowIfInvalid(_action, value);
        _entity = value;
      }
    }

    public SyncEntity Data { get; set; }

    private void ThrowIfInvalid(SyncLogAction action, SyncEntityType entity) {
      if (action == SyncLogAction.Delete) {
        switch (entity) {
          case SyncEntityType.Event:
          case SyncEntityType.Organization:
          case SyncEntityType.User:
            throw new InvalidOperationException($"It is not valid to {action} a {entity}.");
        }
      }
    }
  }

  public class SyncResponse : SyncMessageBase {
    public override string MessageType { get { return "sync"; } set { } }

    public IEnumerable<SyncLogEntry> Logs { get; set; }
    public VersionDetails Versions { get; set; }
    public long Remaining { get; set; }
  }
}
