CREATE TABLE [dbo].[ApplicationSettings] (
  [Id]    NVARCHAR(255) NOT NULL,
  [Value] NVARCHAR(MAX) NULL,
  CONSTRAINT [PK_ApplicationSettings] PRIMARY KEY CLUSTERED ([Id]),
)
