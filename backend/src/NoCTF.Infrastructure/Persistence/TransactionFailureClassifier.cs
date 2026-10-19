using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Persistence;

public static class TransactionFailureClassifier
{
    public static bool IsDependencyUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is TimeoutException
                or DbException { SqlState: "53300" or "53400" or "57014" or "57P01" or "57P02" or "57P03" }
                || current is DbException database && (database.SqlState?.StartsWith("08", StringComparison.Ordinal) == true
                    || database.SqlState is null && database.IsTransient))
                return true;
        return false;
    }

    public static bool IsRetryable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or DbException { SqlState: "40001" or "40P01" })
                return true;
        return false;
    }
}
