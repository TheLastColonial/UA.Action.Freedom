/*
    How a volunteer is shown wherever a record names one: the crew of a vehicle, and the "last
    changed by" every entity carries (ADR 0017).

    An erased volunteer keeps their dbo.Person identity when a past record still points at it, but
    their dbo.PersonDetail row is gone, so the name comes back as "Former volunteer" and nothing
    links it to anyone. The LEFT JOIN is the whole rule, stated once here instead of in every query
    that shows a name. The app's schema-level SELECT on dbo covers the view; it exposes no column
    the dbo.PersonDetail grant did not already.
*/
CREATE VIEW [dbo].[PersonDisplay]
AS
SELECT
    p.[Id]                                                      AS [PersonId],
    COALESCE(d.[FirstName], N'Former')                          AS [FirstName],
    COALESCE(d.[LastName], N'volunteer')                        AS [LastName],
    COALESCE(d.[FirstName] + N' ' + d.[LastName], N'Former volunteer') AS [DisplayName]
FROM [dbo].[Person] AS p
LEFT JOIN [dbo].[PersonDetail] AS d ON d.[PersonId] = p.[Id];
GO
