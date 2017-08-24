namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using Orleans;
  using Orleans.Messaging;
  using Orleans.Runtime;
  using Orleans.Runtime.Configuration;

  public class SqlMembershipProvider : IMembershipTable, IGatewayListProvider {
    public TimeSpan MaxStaleness => throw new NotImplementedException();

    public bool IsUpdatable => throw new NotImplementedException();

    public Task DeleteMembershipTableEntries(string deploymentId) {
      throw new NotImplementedException();
    }

    public Task<IList<Uri>> GetGateways() {
      throw new NotImplementedException();
    }

    public Task InitializeGatewayListProvider(ClientConfiguration clientConfiguration, Logger logger) {
      throw new NotImplementedException();
    }

    public Task InitializeMembershipTable(GlobalConfiguration globalConfiguration, bool tryInitTableVersion, Logger logger) {
      throw new NotImplementedException();
    }

    public Task<bool> InsertRow(MembershipEntry entry, TableVersion tableVersion) {
      throw new NotImplementedException();
    }

    public Task<MembershipTableData> ReadAll() {
      throw new NotImplementedException();
    }

    public Task<MembershipTableData> ReadRow(SiloAddress key) {
      throw new NotImplementedException();
    }

    public Task UpdateIAmAlive(MembershipEntry entry) {
      throw new NotImplementedException();
    }

    public Task<bool> UpdateRow(MembershipEntry entry, string etag, TableVersion tableVersion) {
      throw new NotImplementedException();
    }
  }
}
