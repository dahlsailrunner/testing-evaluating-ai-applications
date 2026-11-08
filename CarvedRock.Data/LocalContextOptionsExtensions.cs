using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Pgvector.EntityFrameworkCore;

namespace CarvedRock.Data;

public static class LocalContextOptionsExtensions
{
    /// <summary>
    /// Use this instead of <c>UseNpgsql</c> when you build a <see cref="LocalContext"/> by hand
    /// (tests, tools): the model contains a pgvector column, and without <c>UseVector()</c> EF
    /// can't even build the model. (The API gets the same effect from its AddNpgsqlDbContext
    /// call in Program.cs.)
    /// </summary>
    public static DbContextOptionsBuilder<LocalContext> UseCarvedRockNpgsql(
        this DbContextOptionsBuilder<LocalContext> builder, string? connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(connectionString); // e.g. a resource that has no connection string
        return builder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.UseVector();
            configure?.Invoke(npgsql);
        });
    }
}
