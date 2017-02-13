CREATE PROCEDURE [dbo].[UpdateRateLimit]
  @Token NVARCHAR(64),
  @RateLimit INT,
  @RateLimitRemaining INT,
  @RateLimitReset DATETIMEOFFSET
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  UPDATE Accounts SET
    RateLimit = @RateLimit,
    RateLimitRemaining =  @RateLimitRemaining,
    RateLimitReset = @RateLimitReset
  FROM GitHubTokens AS g
    INNER LOOP JOIN Accounts AS a ON (a.Id = g.UserId)
  WHERE g.Token = @Token
    AND (
      (@RateLimitRemaining < RateLimitRemaining AND RateLimitReset = @RateLimitReset) -- Same window, fewer remaining
      OR
      (@RateLimitReset > RateLimitReset ) -- New, future window
    )
  OPTION (FORCE ORDER)
END
