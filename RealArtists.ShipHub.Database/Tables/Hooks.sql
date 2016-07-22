CREATE TABLE [dbo].[Hooks] (
  [Id]               BIGINT           NOT NULL,
  [Secret]           UNIQUEIDENTIFIER NOT NULL,
  [Active]           BIT              NOT NULL,
  [Events]           NVARCHAR(500)    NOT NULL,
  [LastSeen]         DATETIMEOFFSET   NULL,
  [CreatorAccountId] BIGINT           NOT NULL,
  [RepositoryId]     BIGINT           NULL,
  [OrganizationId]   BIGINT           NULL,
  CONSTRAINT [PK_Hooks] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [CK_Hooks_Linked] CHECK ([RepositoryId] IS NOT NULL OR [OrganizationId] IS NOT NULL),
  CONSTRAINT [FK_Hooks_CreatorAccountId_Accounts_Id] FOREIGN KEY ([CreatorAccountId]) REFERENCES [dbo].[Accounts]([Id]), 
  CONSTRAINT [FK_Hooks_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_Hooks_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts]([Id])
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Hooks_RepositoryId]
  ON [dbo].[Hooks] ([RepositoryId])
  WHERE ([RepositoryId] IS NOT NULL)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Hooks_OrganizationId]
  ON [dbo].[Hooks] ([OrganizationId])
  WHERE ([OrganizationId] IS NOT NULL)
GO
