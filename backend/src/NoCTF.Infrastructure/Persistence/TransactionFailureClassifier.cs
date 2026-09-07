using System.Data.Common;

namespace NoCTF.Infrastructure.Persistence;

public static class TransactionFailureClassifier
{
    public static bool IsRetryable(Exception exception)
    {
        if (exception is FeatureCriticalSectionTimeoutException) return true;
        while (exception.InnerException is not null) exception = exception.InnerException;
        return exception is DbException { SqlState: "40P01" or "55P03" or "40001" };
    }
}
