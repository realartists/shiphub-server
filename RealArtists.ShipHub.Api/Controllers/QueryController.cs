namespace RealArtists.ShipHub.Api.Controllers {
  using System;
  using System.Data.Entity;
  using System.Linq;
  using System.Threading.Tasks;
  using System.Web.Http;
  using System.Web.Http.Cors;
  using AutoMapper;
  using RealArtists.ShipHub.Api.Sync.Messages.Entries;
  using RealArtists.ShipHub.Common;
  using RealArtists.ShipHub.Common.DataModel;
  using RealArtists.ShipHub.QueueClient;

  public class ShortQueryResponse {
    public string Identifier { get; set; }
    public string Title { get; set; }
    public string Predicate { get; set; }
    public long Author { get; set; }
  }

  public class QueryBody {
    public string Title { get; set; }
    public string Predicate { get; set; }
  }

  [RoutePrefix("api/query")]
  public class QueryController : ShipHubApiController {
    private IMapper _mapper;
    private IShipHubQueueClient _queueClient;

    public QueryController(IMapper mapper, IShipHubQueueClient queueClient) {
      _mapper = mapper;
      _queueClient = queueClient;
    }

    private async Task<QueryEntry> LookupQuery(ShipHubContext context, Guid id) {
      var q = await context.Queries
        .AsNoTracking()
        .Include(x => x.Author)
        .Where(x => x.Id == id)
        .SingleOrDefaultAsync();
      if (q != null) {
        return new QueryEntry() {
          Id = q.Id,
          Title = q.Title,
          Predicate = q.Predicate,
          Author = new AccountEntry() {
            Identifier = q.Author.Id,
            Login = q.Author.Login,
            Name = q.Author.Name
          }
        };
      }
      return null;
    }

    [HttpGet]
    [HttpOptions]
    [AllowAnonymous]
    [Route("{queryId:guid}")]
    [EnableCors(origins: "https://ship.realartists.com", headers: "Accept,Origin,Content-Type", methods: "GET,OPTIONS", exposedHeaders: "", PreflightMaxAge = 300,  SupportsCredentials= false)]
    public async Task<IHttpActionResult> QueryInfo(Guid queryId) {
      QueryEntry entry = null;
      using (var context = new ShipHubContext()) {
        entry = await LookupQuery(context, queryId);
      }

      if (entry != null) {
        return Ok(entry);
      } else {
        return NotFound();
      }
    }

    [HttpPut]
    [Route("{queryId:guid}")]
    public async Task<IHttpActionResult> SaveQuery(string queryId, [FromBody] QueryBody query) {
      var id = Guid.Parse(queryId);

      using (var context = new ShipHubContext()) {
        var updater = new DataUpdater(context, _mapper);
        await updater.UpdateQuery(id, ShipHubUser.UserId, query.Title, query.Predicate);
        // If there are no changes, then the author ID didn't match
        if (updater.Changes.IsEmpty) {
          return StatusCode(System.Net.HttpStatusCode.Conflict);
        }
        _queueClient.NotifyChanges(updater.Changes).LogFailure(ShipHubUser.DebugIdentifier);
      }

      var ret = new ShortQueryResponse() {
        Identifier = queryId,
        Title = query.Title,
        Predicate = query.Predicate,
        Author = ShipHubUser.UserId
      };

      return Ok(ret);
    }

    [HttpDelete]
    [Route("{queryId:guid}")]
    public async Task<IHttpActionResult> UnwatchQuery(Guid queryId) {
      using (var context = new ShipHubContext()) {
        var updater = new DataUpdater(context, _mapper);
        await updater.ToggleWatchQuery(queryId, ShipHubUser.UserId, false);
        _queueClient.NotifyChanges(updater.Changes).LogFailure(ShipHubUser.DebugIdentifier);
      }

      return StatusCode(System.Net.HttpStatusCode.NoContent);
    }

    [HttpPut]
    [Route("{queryId:guid}/watch")]
    public async Task<IHttpActionResult> WatchQuery(Guid queryId) {
      QueryEntry entry = null;
      using (var context = new ShipHubContext()) {
        entry = await LookupQuery(context, queryId);
        if (entry != null) {
          var updater = new DataUpdater(context, _mapper);
          await updater.ToggleWatchQuery(queryId, ShipHubUser.UserId, true);
          _queueClient.NotifyChanges(updater.Changes).LogFailure(ShipHubUser.DebugIdentifier);
        }
      }

      if (entry != null) {
        return Ok(entry);
      } else {
        return NotFound();
      }
    }
  }
}
