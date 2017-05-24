namespace RealArtists.ShipHub.Common {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text;
  using System.Threading.Tasks;
  using AutoMapper;
  using DataModel;
  using DataModel.Types;

  /// <summary>
  /// NOTE: MUST BE STATELESS!
  /// All state must reside in the update batch itself
  /// </summary>
  public class UpdateBatchProcessor {
    private IMapper _mapper;
    private IFactory<ShipHubContext> _contextFactory;

    public UpdateBatchProcessor(IMapper mapper, IFactory<ShipHubContext> contextFactory) {
      _mapper = mapper;
      _contextFactory = contextFactory;
    }

    public async Task<IChangeSummary> Submit(UpdateBatch batch) {
      await Task.CompletedTask;
      throw new NotImplementedException();
    }
  }
}
