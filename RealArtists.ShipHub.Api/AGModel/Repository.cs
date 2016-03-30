namespace RealArtists.ShipHub.Api.AGModel {
  using System.Collections.Generic;
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;
  using System.Diagnostics.CodeAnalysis;

  public partial class Repository {
    [SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
    public Repository() {
      Comments = new HashSet<Comment>();
      Events = new HashSet<Event>();
      Issues = new HashSet<Issue>();
      Milestones = new HashSet<Milestone>();
      AssignableAccounts = new HashSet<Account>();
      Labels = new HashSet<Label>();
    }

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    public int AccountId { get; set; }

    public bool Private { get; set; }

    public bool HasIssues { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; }

    [Required]
    [StringLength(255)]
    public string FullName { get; set; }

    [StringLength(500)]
    public string Description { get; set; }

    public string ExtensionJson { get; set; }

    public long? RowVersion { get; set; }

    public long? RestoreVersion { get; set; }

    public virtual Account Account { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Comment> Comments { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Event> Events { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Issue> Issues { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Milestone> Milestones { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Account> AssignableAccounts { get; set; }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public virtual ICollection<Label> Labels { get; set; }
  }
}
