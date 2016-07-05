CREATE TABLE [dbo].[OrganizationLog] (
  [Id]             BIGINT NOT NULL IDENTITY(1,1),
  [OrganizationId] BIGINT NOT NULL,
  [AccountId]      BIGINT NOT NULL,
  [Delete]         BIT    NOT NULL,
  [RowVersion]     BIGINT NOT NULL CONSTRAINT [DF_OrganizationLog_RowVersion] DEFAULT (NEXT VALUE FOR [dbo].[SyncIdentifier]),
  CONSTRAINT [PK_OrganizationLog] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_OrganizationLog_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationLog_OrganizationId] ON [dbo].[OrganizationLog]([OrganizationId])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_OrganizationLog_AccountId_OrganizationId] ON [dbo].[OrganizationLog]([AccountId], [OrganizationId])
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationLog_OrganizationId_RowVersion] ON [dbo].[OrganizationLog]([OrganizationId], [RowVersion])
GO
