CREATE TABLE [dbo].[Subscriptions] (
  [AccountId]    BIGINT NOT NULL,
  [State]        INT NOT NULL,
  [TrialEndDate] DATETIMEOFFSET NULL,
  CONSTRAINT [PK_Subscriptions] PRIMARY KEY CLUSTERED ([AccountId]),
  CONSTRAINT [FK_Subscriptions_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [UIX_Subscriptions_AccountId] ON [dbo].[Subscriptions]([AccountId])
GO
