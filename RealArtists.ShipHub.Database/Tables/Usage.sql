CREATE TABLE [dbo].[Usage] (
  [AccountId] BIGINT         NOT NULL,
  [Date]      DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_Usage] PRIMARY KEY CLUSTERED ([AccountId], [Date]),
  CONSTRAINT [FK_Usage_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO
