CREATE TABLE [dbo].[GitHubMetaData] (
  [Id]            BIGINT         IDENTITY(1, 1) NOT NULL,
  [ETag]          NVARCHAR(64)   NULL,
  [Expires]       DATETIMEOFFSET NULL,
  [LastModified]  DATETIMEOFFSET NULL,
  [LastRefresh]   DATETIMEOFFSET NULL,
  [AccessTokenId] BIGINT         NULL,
  CONSTRAINT [PK_GitHubMetaData] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FKCD_GitHubMetaData_CacheTokenId_AccessTokens_Id] FOREIGN KEY ([AccessTokenId]) REFERENCES [dbo].[AccessTokens] ([Id]) ON DELETE CASCADE,
);
GO

CREATE NONCLUSTERED INDEX [IX_GitHubMetaData_Expires] ON [dbo].[GitHubMetaData]([Expires]);
GO

CREATE NONCLUSTERED INDEX [IX_GitHubMetaData_LastRefresh] ON [dbo].[GitHubMetaData]([LastRefresh]);
GO

CREATE NONCLUSTERED INDEX [IX_GitHubMetaData_AccessTokenId] ON [dbo].[GitHubMetaData]([AccessTokenId]);
GO
