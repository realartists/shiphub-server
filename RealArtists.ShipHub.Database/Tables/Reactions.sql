CREATE TABLE [dbo].[Reactions]  (
  [Id]           BIGINT         NOT NULL,
  [UserId]       BIGINT         NOT NULL,
  [IssueId]      BIGINT         NULL,
  [CommentId]    BIGINT         NULL,
  [Content]      NVARCHAR(15)   NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  CONSTRAINT [CK_Reactions_IssueOrCommentExclusive] CHECK ((IssueId IS NOT NULL AND CommentId IS NULL) OR (IssueId IS NULL AND CommentId IS NOT NULL)),
  CONSTRAINT [PK_Reactions] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Reactions_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Reactions_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_Reactions_CommentId_Comments_Id] FOREIGN KEY ([CommentId]) REFERENCES [dbo].[Comments] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_UserId] ON [dbo].[Reactions]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_IssueId]
  ON [dbo].[Reactions]([IssueId])
  WHERE ([IssueId] IS NOT NULL)
GO

CREATE NONCLUSTERED INDEX [IX_Reactions_CommentId]
  ON [dbo].[Reactions]([CommentId])
  WHERE ([CommentId] IS NOT NULL)
GO
