CREATE TYPE [dbo].[MilestoneTableType] AS TABLE (
  [Id]             BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [Number]         INT            NOT NULL,
  [State]          NVARCHAR(10)   NOT NULL,
  [Title]          NVARCHAR(MAX)  NOT NULL,
  [Description]    NVARCHAR(MAX)  NULL,
  [CreatedAt]      DATETIMEOFFSET NOT NULL,
  [UpdatedAt]      DATETIMEOFFSET NOT NULL,
  [ClosedAt]       DATETIMEOFFSET NULL,
  [DueOn]          DATETIMEOFFSET NULL
)
