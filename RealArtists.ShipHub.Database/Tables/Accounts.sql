CREATE TABLE [dbo].[Accounts] (
  [Id]                   BIGINT         NOT NULL,
  [Type]                 NVARCHAR(4)    NOT NULL,
  [Login]                NVARCHAR(255)  NOT NULL,
  [Date]                 DATETIMEOFFSET NOT NULL,
  [RepositoryMetaDataId] BIGINT         NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Accounts_RepositoryMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([RepositoryMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_Type] ON [dbo].[Accounts]([Type]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Login] ON [dbo].[Accounts]([Login]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_RepositoryMetaDataId]
  ON [dbo].[Accounts]([RepositoryMetaDataId])
  WHERE ([RepositoryMetaDataId] IS NOT NULL);
GO
