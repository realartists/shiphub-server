CREATE TYPE [dbo].[AccountTableType] AS TABLE (
  [Id]        INT            NOT NULL PRIMARY KEY CLUSTERED,
  [Type]      NVARCHAR(4)    NOT NULL,
  [AvatarUrl] NVARCHAR(500)  NULL,
  [Login]     NVARCHAR(255)  NOT NULL
)
