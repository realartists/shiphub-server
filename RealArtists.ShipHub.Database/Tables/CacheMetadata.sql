CREATE TABLE [dbo].[CacheMetadata] (
  [Id]           BIGINT        NOT NULL IDENTITY(1,1),
  [Key]          NVARCHAR(255) NOT NULL,
  [AccessToken]  AS CONVERT(NVARCHAR(64),JSON_VALUE(MetadataJson,'$.accessToken')) PERSISTED,
  [LastRefresh]  AS CONVERT(DATETIMEOFFSET,JSON_VALUE(MetadataJson,'$.lastRefresh'), 127) PERSISTED,
  [MetadataJson] NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [PK_CacheMetadata] PRIMARY KEY CLUSTERED ([Id]),
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_CacheMetadata_Key_Token] ON [dbo].[CacheMetadata] ([Key], AccessToken) INCLUDE (LastRefresh)
GO

-- Used to purge cache entries for expired tokens.
CREATE NONCLUSTERED INDEX [IX_CacheMetadata_Token] ON [dbo].[CacheMetadata] (AccessToken)
GO
