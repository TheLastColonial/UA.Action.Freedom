/*
    How a donor is shown wherever a record names one. An erased donor keeps their dbo.Donor identity while a
    donation still points at it, but their dbo.DonorDetail row is gone, so the name comes back as
    "Former donor". The LEFT JOIN is the whole rule, stated once here (see dbo.PersonDisplay).
*/
CREATE VIEW [dbo].[DonorDisplay]
AS
SELECT
    d.[Id]                           AS [DonorId],
    COALESCE(x.[Name], N'Former donor') AS [DisplayName]
FROM [dbo].[Donor] AS d
LEFT JOIN [dbo].[DonorDetail] AS x ON x.[DonorId] = d.[Id];
GO
