CREATE PROCEDURE [dbo].[SetUserAccessToken]
  @UserId BIGINT,
  @Token NVARCHAR(64),
  @Scopes NVARCHAR(255),
  @RateLimit INT,
  @RateLimitRemaining INT,
  @RateLimitReset DATETIMEOFFSET
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  UPDATE Accounts SET
    Token = @Token,
    Scopes = @Scopes,
    RateLimit = @RateLimit,
    RateLimitRemaining =  @RateLimitRemaining,
    RateLimitReset = @RateLimitReset
  WHERE Id = @UserId
END
