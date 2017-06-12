CREATE TABLE [dbo].[Hooks] (
  [Id]               BIGINT           NOT NULL IDENTITY(1, 1),
  [GitHubId]         BIGINT           NULL,
  [Secret]           UNIQUEIDENTIFIER NOT NULL,
  [Events]           NVARCHAR(500)    NOT NULL,
  [LastSeen]         DATETIMEOFFSET   NULL,
  [LastPing]         DATETIMEOFFSET   NULL,
  [PingCount]        INT              NULL,
  [RepositoryId]     BIGINT           NULL,
  [OrganizationId]   BIGINT           NULL,
  [LastError]        DATETIMEOFFSET   NULL,
  CONSTRAINT [PK_Hooks] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [CK_Hooks_Linked] CHECK ([RepositoryId] IS NOT NULL OR [OrganizationId] IS NOT NULL),
  CONSTRAINT [FK_Hooks_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_Hooks_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_Hooks_LastSeen_LastPing]
  ON [dbo].[Hooks] ([LastSeen], [LastPing])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Hooks_RepositoryId]
  ON [dbo].[Hooks] ([RepositoryId])
  WHERE ([RepositoryId] IS NOT NULL)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Hooks_OrganizationId]
  ON [dbo].[Hooks] ([OrganizationId])
  WHERE ([OrganizationId] IS NOT NULL)
GO
