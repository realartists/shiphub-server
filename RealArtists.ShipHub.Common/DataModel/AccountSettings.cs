namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using RealArtists.ShipHub.Common.DataModel.Types;

  [Table("AccountSettings")]
  public class AccountSettings {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long AccountId { get; set; }

    public string SyncSettingsJson {
      get => SyncSettings.SerializeObject();
      set => SyncSettings = value?.DeserializeObject<SyncSettings>();
    }

    [NotMapped]
    public SyncSettings SyncSettings { get; set; }

    public virtual Account Account { get; set; }
  }
}
