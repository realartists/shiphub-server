CREATE TABLE [dbo].[GitHubInstallations] (
  [Id]                     BIGINT         NOT NULL,
  [AccountId]              BIGINT         NOT NULL,
  [RepositorySelection]    NVARCHAR(20)   NOT NULL, -- "selected" or "all"
  [RepositoryMetadataJson] NVARCHAR(MAX)  NULL,
  [LastSeen]               DATETIMEOFFSET NOT NULL
  CONSTRAINT [PK_GitHubInstallations] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_GitHubInstallations_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id])
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_GitHubInstallations_AccountId] ON [dbo].[GitHubInstallations]([AccountId])
GO
