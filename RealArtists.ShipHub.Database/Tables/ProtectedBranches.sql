CREATE TABLE [dbo].[ProtectedBranches]
(
  [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED IDENTITY(1,1),
  [RepositoryId] BIGINT NOT NULL,
  [Name] NVARCHAR(255) NOT NULL, -- Technically, git allows longer, but GitHub and most filesystems are capped at 255.
  [Protection] NVARCHAR(MAX) NOT NULL,
  [ProtectionMetadataJson] NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [FK_Branches_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id])
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Branches_RepositoryId_Name] ON [dbo].[ProtectedBranches]([RepositoryId], [Name])
GO
