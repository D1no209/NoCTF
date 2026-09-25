using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace NoCTF.Infrastructure.Persistence;

public static class TransactionFailureClassifier
{
    public static bool IsRetryable(Exception exception)
    {
        if (exception is FeatureCriticalSectionTimeoutException) return true;
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or DbException { IsTransient: true })
                return true;
        return false;
    }
}
