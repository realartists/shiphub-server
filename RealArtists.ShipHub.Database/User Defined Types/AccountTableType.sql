CREATE TYPE [dbo].[AccountTableType] AS TABLE (
  [Id]        BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [Type]      NVARCHAR(4)    NOT NULL,
  [Login]     NVARCHAR(255)  NOT NULL
)
