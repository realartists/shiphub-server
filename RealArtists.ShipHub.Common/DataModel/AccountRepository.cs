﻿namespace RealArtists.ShipHub.Common.DataModel {
  using System.ComponentModel.DataAnnotations;
  using System.ComponentModel.DataAnnotations.Schema;

  public class AccountRepository {
    [Key]
    [Column(Order = 0)]
    public long AccountId { get; set; }

    [Key]
    [Column(Order = 1)]
    public long RepositoryId { get; set; }

    public bool Admin { get; set; }
    public bool Push { get; set; }
    public bool Pull { get; set; }

    public virtual User Account { get; set; }

    public virtual Repository Repository { get; set; }
  }
}
