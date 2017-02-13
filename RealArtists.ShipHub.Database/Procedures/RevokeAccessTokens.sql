CREATE PROCEDURE [dbo].[RevokeAccessTokens]
  @UserId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM GitHubTokens WHERE UserId = @UserId

  UPDATE Accounts SET
    Scopes = DEFAULT,
    RateLimit = DEFAULT,
    RateLimitRemaining =  DEFAULT,
    RateLimitReset = DEFAULT
  OUTPUT 'user' AS ItemType, INSERTED.Id AS ItemId
  WHERE Id = @UserId
END
