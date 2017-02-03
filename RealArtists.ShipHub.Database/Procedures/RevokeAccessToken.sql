CREATE PROCEDURE [dbo].[RevokeAccessToken]
  @Token NVARCHAR(64)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  UPDATE Accounts SET
    Token = DEFAULT,
    Scopes = DEFAULT,
    RateLimit = DEFAULT,
    RateLimitRemaining =  DEFAULT,
    RateLimitReset = DEFAULT
  WHERE Token = @Token
END
