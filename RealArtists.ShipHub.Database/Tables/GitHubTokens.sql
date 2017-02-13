CREATE TABLE [dbo].[GitHubTokens] (
  [Token] NVARCHAR(64)NOT NULL,
  [UserId] BIGINT NOT NULL,
  CONSTRAINT [PK_GitHubTokens] PRIMARY KEY CLUSTERED ([Token] ASC),
  CONSTRAINT [FK_GitHubTokens_AccountId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id])
)
GO

CREATE NONCLUSTERED INDEX [UIX_GitHubTokens_AccountId] ON [dbo].[GitHubTokens]([UserId])
GO
