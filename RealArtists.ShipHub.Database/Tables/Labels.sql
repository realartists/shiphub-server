CREATE TABLE [dbo].[Labels] (
  [Id]    BIGINT        NOT NULL,
  [RepositoryId] BIGINT NOT NULL,
  [Color] CHAR(6)       NOT NULL,
  [Name]  NVARCHAR(400) NOT NULL,
  CONSTRAINT [PK_Labels] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_Labels_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
)
GO