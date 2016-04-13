CREATE TABLE [dbo].[AccessTokens] (
  [Id]                 BIGINT         IDENTITY(1, 1) NOT NULL,
  [AccountId]          INT            NOT NULL,
  [ApplicationId]      NVARCHAR(64)   NOT NULL,
  [Token]              NVARCHAR(64)   NOT NULL,
  [Scopes]             NVARCHAR(255)  NOT NULL,
  [RateLimit]          INT            NOT NULL,
  [RateLimitRemaining] INT            NOT NULL,
  [RateLimitReset]     DATETIMEOFFSET NOT NULL,
  [CreatedAt]          DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_AccessTokens] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_AccessTokens_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_AccessTokens_AccountId] ON [dbo].[AccessTokens]([AccountId]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_AccessTokens_Token] ON [dbo].[AccessTokens]([Token]);
GO

CREATE TRIGGER [dbo].[TRG_AccessTokens_Delete]
ON [dbo].[AccessTokens]
INSTEAD OF DELETE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  UPDATE g SET
      AccessTokenId = NULL,
      ETag = NULL,
      Expires = NULL,
      LastModified = NULL
    FROM GitHubMetaData as g
      INNER JOIN deleted as d ON (g.AccessTokenId = d.Id)
    WHERE g.AccessTokenId IS NOT NULL

  DELETE FROM AccessTokens
    WHERE Id IN (SELECT Id FROM deleted)
END
GO
