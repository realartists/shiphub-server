CREATE TABLE [dbo].[GitHubTokens] (
  [Token]   NVARCHAR(64) NOT NULL,
  [UserId]  BIGINT       NOT NULL,
  [Version] INT          NOT NULL DEFAULT 1
  CONSTRAINT [PK_GitHubTokens] PRIMARY KEY CLUSTERED ([Token] ASC),
  CONSTRAINT [FK_GitHubTokens_AccountId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_GitHubTokens_AccountId] ON [dbo].[GitHubTokens]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_GitHubTokens_Version] ON [dbo].[GitHubTokens]([Version])
GO
