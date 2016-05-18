CREATE TYPE [dbo].[MilestoneTableType] AS TABLE (
  [Id]             INT            NOT NULL PRIMARY KEY CLUSTERED,
  [Number]         INT            NOT NULL,
  [State]          NVARCHAR(10)   NOT NULL,
  [Title]          NVARCHAR(255)  NOT NULL,
  [Description]    NVARCHAR(255)  NOT NULL,
  [CreatedAt]      DATETIMEOFFSET NOT NULL,
  [UpdatedAt]      DATETIMEOFFSET NOT NULL,
  [ClosedAt]       DATETIMEOFFSET NULL,
  [DueOn]          DATETIMEOFFSET NULL
)
