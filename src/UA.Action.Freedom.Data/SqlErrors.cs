namespace UA.Action.Freedom.Data;

/// <summary>SQL Server error numbers the repositories translate into outcomes rather than 500s.</summary>
internal static class SqlErrors
{
    /// <summary>A DELETE or UPDATE conflicted with a FOREIGN KEY or REFERENCE constraint.</summary>
    public const int ForeignKeyViolation = 547;
}
