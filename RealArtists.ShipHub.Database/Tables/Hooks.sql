CREATE TABLE [dbo].[Hooks] (
  [Id]        BIGINT           NOT NULL,
  [Secret]    UNIQUEIDENTIFIER NOT NULL,
  [Active]    BIT              NOT NULL,
  [Events]    NVARCHAR(500)    NOT NULL,
  [LastSeen]  DATETIMEOFFSET   NULL,
  [CreatorAccountId] BIGINT    NOT NULL, 
  [RepositoryId] BIGINT        NULL, 
  [OrganizationId] BIGINT      NULL, 
  CONSTRAINT [FK_Hooks_CreatorAccountId_Accounts_Id] FOREIGN KEY ([CreatorAccountId]) REFERENCES [Accounts]([Id]), 
  CONSTRAINT [FK_Hooks_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [Repositories]([Id]),
  CONSTRAINT [FK_Hooks_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [Accounts]([Id]), 
  CONSTRAINT [PK_Hooks] PRIMARY KEY ([Id])
)

GO

CREATE INDEX [IX_Hooks_RepositoryId] ON [dbo].[Hooks] ([RepositoryId])
GO

CREATE INDEX [IX_Hooks_OrganizationId] ON [dbo].[Hooks] ([OrganizationId])
GO
