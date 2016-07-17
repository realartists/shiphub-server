using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RealArtists.ShipHub.Common.DataModel {
  public class Hook {
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long Id { get; set; }

    public Guid Secret { get; set; }

    public bool Active { get; set; }

    [StringLength(500)]
    public string Events { get; set; }

    public DateTimeOffset? LastSeen { get; set; }

    public long CreatorAccountId { get; set; }
    public long? RepositoryId { get; set; }
    public long? OrganizationId { get; set; }

    public virtual Repository Repository { get; set; }
    public virtual Organization Organization { get; set; }
  }
}
