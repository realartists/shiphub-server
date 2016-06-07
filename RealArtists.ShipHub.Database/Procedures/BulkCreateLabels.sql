CREATE PROCEDURE [dbo].[BulkCreateLabels]
  @Labels LabelTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Labels WITH (SERIALIZABLE) as [Target]
  USING (SELECT Color, Name FROM @Labels) as [Source]
  ON ([Target].Color = [Source].Color AND [Target].Name = [Source].Name)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Color, Name)
    VALUES (Color, Name);
END
