CREATE TYPE [dbo].[SubscriptionTableType] AS TABLE (
  [AccountId]    BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [State]        NVARCHAR(15)   NOT NULL,
  [TrialEndDate] DATETIMEOFFSET NULL,
  [Version]      BIGINT         NOT NULL
)
