CREATE PROCEDURE [dbo].[UpdateRateLimit]
  @Token NVARCHAR(64),
  @RateLimit INT,
  @RateLimitRemaining INT,
  @RateLimitReset DATETIMEOFFSET
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;
  
  DECLARE @Updated bit = 0

  UPDATE AccessTokens SET
    @Updated = 1,
    RateLimit = @RateLimit,
    RateLimitRemaining =  @RateLimitRemaining
  WHERE Token = @Token
    AND RateLimitReset = @RateLimitReset
    AND (@RateLimit < RateLimit OR @RateLimitRemaining < RateLimitRemaining)

  IF(@Updated = 0) BEGIN
    UPDATE AccessTokens SET
      RateLimit = @RateLimit,
      RateLimitRemaining = @RateLimitRemaining,
      RateLimitReset = @RateLimitReset
    WHERE Token = @Token AND RateLimitReset < @RateLimitReset
  END

  RETURN 0
END
