using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Persistence;

internal static class RelationalRetry
{
    public static bool IsTransientConcurrency(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException
                || current is DbException { IsTransient: true })
                return true;
        }
        return false;
    }
}
