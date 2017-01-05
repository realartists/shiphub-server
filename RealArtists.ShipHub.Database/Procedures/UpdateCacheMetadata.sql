CREATE PROCEDURE [dbo].[UpdateCacheMetadata]
  @Key NVARCHAR(255),
  @MetadataJson NVARCHAR(MAX)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  MERGE INTO CacheMetadata AS [Target]
  USING (VALUES(
    @Key,
    @MetadataJson,
    CONVERT(NVARCHAR(64),JSON_VALUE(@MetadataJson,'$.accessToken')),
    CONVERT(DATETIMEOFFSET,JSON_VALUE(@MetadataJson,'$.lastRefresh'), 127)))
  AS [Source] ([Key], MetadataJson, AccessToken, LastRefresh)
  ON [Target].[Key] = [Source].[Key]
    AND [Target].AccessToken = [Source].AccessToken
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Key], MetadataJson)
    VALUES ([Key], MetadataJson)
  -- Update
  WHEN MATCHED 
    AND [Target].LastRefresh < [Source].LastRefresh THEN
    UPDATE SET
      [Key] = [Source].[Key],
      MetadataJson = [Source].MetadataJson
  OPTION (LOOP JOIN, FORCE ORDER);
END
