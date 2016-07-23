CREATE PROCEDURE [dbo].[BulkCreateLabels]
  @Labels LabelTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  INSERT INTO Labels WITH (SERIALIZABLE) (Color, Name)
  SELECT DISTINCT l.Color, l.Name
  FROM @Labels as l
  WHERE NOT EXISTS (SELECT 1 FROM Labels WHERE Color = l.Color AND Name = l.Name)
  OPTION (RECOMPILE);
END
