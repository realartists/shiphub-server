CREATE TYPE [dbo].[MetaDataTableType] AS TABLE (
  [ItemId]       BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [Id]           BIGINT         NULL,
  [ETag]         NVARCHAR(64)   NULL,
  [Expires]      DATETIMEOFFSET NULL,
  [LastModified] DATETIMEOFFSET NULL,
  [LastRefresh]  DATETIMEOFFSET NULL,
  [AccountId]    BIGINT         NULL
)
