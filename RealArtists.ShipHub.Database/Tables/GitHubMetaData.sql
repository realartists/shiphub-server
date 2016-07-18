CREATE TABLE [dbo].[GitHubMetaData] (
  [Id]            BIGINT         IDENTITY(1, 1) NOT NULL,
  [ETag]          NVARCHAR(64)   NULL,
  [Expires]       DATETIMEOFFSET NULL,
  [LastModified]  DATETIMEOFFSET NULL,
  [LastRefresh]   DATETIMEOFFSET NULL,
  [AccountId]     BIGINT         NULL,
  CONSTRAINT [PK_GitHubMetaData] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_GitHubMetaData_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_GitHubMetaData_Expires] ON [dbo].[GitHubMetaData]([Expires])
GO

CREATE NONCLUSTERED INDEX [IX_GitHubMetaData_LastRefresh] ON [dbo].[GitHubMetaData]([LastRefresh])
GO

CREATE NONCLUSTERED INDEX [IX_GitHubMetaData_AccountId] ON [dbo].[GitHubMetaData]([AccountId])
GO
