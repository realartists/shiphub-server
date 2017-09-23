CREATE TABLE [dbo].[Queries] (
  [Id]        UNIQUEIDENTIFIER NOT NULL,
  [AuthorId]  BIGINT           NOT NULL,
  [Title]     NVARCHAR(255)    NOT NULL,
  [Predicate] NVARCHAR(MAX)    NOT NULL,
  CONSTRAINT [PK_Queries] PRIMARY KEY ([Id]),
  CONSTRAINT [FK_Queries_AuthorId_Accounts_Id] FOREIGN KEY ([AuthorId]) REFERENCES [dbo].[Accounts] ([Id])
)
GO
