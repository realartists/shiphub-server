using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace RealArtists.ShipHub.Common.DataModel {
  [Table("Queries")]
  public class Query {
    public Guid Id { get; set; }

    public string Title { get; set; }

    public string Predicate { get; set; }

    public long AuthorId { get; set; }

    public virtual Account Author { get; set; }
  }
}
