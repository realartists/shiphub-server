using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace RealArtists.ShipHub.Common {
  /// <summary>
  /// ChargeBeePersonalSubscriptionMetaData objects get serialized and stored with
  /// each subscription in ChargeBee.  If changing the schema, be aware that
  /// older versions of this object still exist in ChargeBee.
  /// </summary>
  public class ChargeBeePersonalSubscriptionMetaData {
    public int? TrialPeriodDays { get; set; }
  }
}
