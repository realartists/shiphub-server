CREATE TABLE [dbo].[Queries]
(
  [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
  [AuthorId] BIGINT NOT NULL,
  [Title] NVARCHAR(255)  NOT NULL,
  [Predicate] NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [FK_Queries_AuthorId_Accounts_Id] FOREIGN KEY ([AuthorId]) REFERENCES [dbo].[Accounts] ([Id])
)
GO
