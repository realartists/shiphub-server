CREATE TABLE [dbo].[AccountOrganizations] (
  [UserId]         BIGINT NOT NULL,
  [OrganizationId] BIGINT NOT NULL,
  [Admin]          BIT    NOT NULL,
  CONSTRAINT [PK_AccountOrganizations] PRIMARY KEY CLUSTERED ([UserId], [OrganizationId]),
  CONSTRAINT [FK_AccountOrganizations_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_AccountOrganizations_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_AccountOrganizations_UserId] ON [dbo].[AccountOrganizations]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_AccountOrganizations_OrganizationId] ON [dbo].[AccountOrganizations]([OrganizationId])
GO
