namespace UA.Action.Freedom.Data;

/// <summary>SQL Server error numbers the repositories translate into outcomes rather than 500s.</summary>
internal static class SqlErrors
{
    /// <summary>A DELETE or UPDATE conflicted with a FOREIGN KEY or REFERENCE constraint.</summary>
    public const int ForeignKeyViolation = 547;

    /// <summary>A duplicate key in a unique index.</summary>
    public const int UniqueIndexViolation = 2601;

    /// <summary>A duplicate key in a unique constraint or primary key.</summary>
    public const int UniqueConstraintViolation = 2627;
}
