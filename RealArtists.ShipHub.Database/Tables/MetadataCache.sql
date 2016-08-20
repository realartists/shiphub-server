CREATE TABLE [dbo].[MetadataCache] (
  [Id] INT NOT NULL IDENTITY(1,1),
  [Key] NVARCHAR(255) NOT NULL,
  [AccessToken] AS JSON_VALUE(MetadataJson, '$.accessToken') PERSISTED,
  [MetadataJson] NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [PK_MetadataCache] PRIMARY KEY CLUSTERED ([Id]),
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_MetadataCache_Key_Token] ON [dbo].[MetadataCache] ([Key], [AccessToken])
GO

-- Used to purge cache entries for expired tokens.
CREATE UNIQUE NONCLUSTERED INDEX [UIX_MetadataCache_Token] ON [dbo].[MetadataCache] ([AccessToken])
GO
