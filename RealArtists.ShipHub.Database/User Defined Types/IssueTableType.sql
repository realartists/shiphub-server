CREATE TYPE [dbo].[IssueTableType] AS TABLE (
  [Id]           INT            NOT NULL PRIMARY KEY CLUSTERED,
  [UserId]       INT            NOT NULL,
  [Number]       INT            NOT NULL,
  [State]        NVARCHAR(6)    NOT NULL,
  [Title]        NVARCHAR(255)  NOT NULL,
  [Body]         NVARCHAR(MAX)  NULL,
  [AssigneeId]   INT            NULL,
  [MilestoneId]  INT            NULL,
  [Locked]       BIT            NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  [ClosedAt]     DATETIMEOFFSET NULL,
  [ClosedById]   INT            NULL,
  [Reactions]    NVARCHAR(300)  NULL
)
