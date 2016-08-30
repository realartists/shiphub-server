CREATE TABLE [dbo].[OrganizationAccounts] (
  [OrganizationId] BIGINT NOT NULL,
  [UserId]         BIGINT NOT NULL,
  [Admin]          BIT    NOT NULL,
  CONSTRAINT [PK_OrganizationAccounts] PRIMARY KEY CLUSTERED ([OrganizationId], [UserId]),
  CONSTRAINT [FK_OrganizationAccounts_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_OrganizationAccounts_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationAccounts_UserId] ON [dbo].[OrganizationAccounts]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_OrganizationAccounts_OrganizationId] ON [dbo].[OrganizationAccounts]([OrganizationId])
GO
