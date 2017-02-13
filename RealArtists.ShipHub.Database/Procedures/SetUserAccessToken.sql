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

  MERGE INTO GitHubTokens AS [Target]
  USING (SELECT @Token AS Token, @UserId AS UserId) AS [Source]
  ON [Target].Token = [Source].Token AND [Target].UserId = [Source].UserId
  WHEN NOT MATCHED BY TARGET THEN
    INSERT(Token, UserId)
    VALUES(Token, UserId)
  OPTION (LOOP JOIN, FORCE ORDER);

  UPDATE Accounts SET
    Scopes = @Scopes,
    RateLimit = @RateLimit,
    RateLimitRemaining =  @RateLimitRemaining,
    RateLimitReset = @RateLimitReset
  WHERE Id = @UserId
END
