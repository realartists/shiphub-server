CREATE TABLE [dbo].[AccountSettings] (
  [AccountId]        BIGINT        NOT NULL,
  [SyncSettingsJson] NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [PK_AccountSettings] PRIMARY KEY CLUSTERED ([AccountId] ASC),
  CONSTRAINT [FK_AccountSettings_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id])
)
GO
