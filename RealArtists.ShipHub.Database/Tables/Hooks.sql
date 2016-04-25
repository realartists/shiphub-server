CREATE TABLE [dbo].[Hooks] (
  [Id]        INT              NOT NULL, 
  [Key]       UNIQUEIDENTIFIER NOT NULL, 
  [Active]    BIT              NOT NULL, 
  [Events]    NVARCHAR(500)    NOT NULL, 
  [LastSeen]  DATETIMEOFFSET   NULL, 
  [Url]       NVARCHAR(500)    NULL, 
  [ConfigUrl] NVARCHAR(500)    NOT NULL
)
