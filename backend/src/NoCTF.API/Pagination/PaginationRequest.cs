using FastEndpoints;
using FluentValidation;

namespace NoCTF.API.Pagination;

/// <summary>
/// Common offset pagination query parameters.
/// </summary>
public class PaginationRequest
{
    [QueryParam]
    public int Offset { get; set; }

    [QueryParam]
    public int Limit { get; set; } = 10;

    [QueryParam]
    public bool Desc { get; set; }
}

/// <summary>
/// Common offset pagination parameters with a free-text filter.
/// </summary>
public class SearchRequest : PaginationRequest
{
    [QueryParam]
    public string? Keyword { get; set; }
}

/// <summary>
/// The common list response contract.
/// </summary>
public class ArrayResult<T>
{
    public ArrayResult() { }

    public ArrayResult(T[] items, int total)
    {
        Items = items;
        Total = total;
    }

    public int Total { get; set; }
    public T[] Items { get; set; } = [];
}

public static class PaginationRules
{
    public const int MaximumLimit = 200;
    public const int MaximumKeywordLength = 256;

    public static void Add<TRequest>(
        AbstractValidator<TRequest> validator)
        where TRequest : PaginationRequest
    {
        validator.RuleFor(request => request.Offset)
            .GreaterThanOrEqualTo(0);
        validator.RuleFor(request => request.Limit)
            .InclusiveBetween(1, MaximumLimit);
    }

    public static void AddSearch<TRequest>(
        AbstractValidator<TRequest> validator)
        where TRequest : SearchRequest
    {
        Add<TRequest>(validator);
        validator.RuleFor(request => request.Keyword)
            .MaximumLength(MaximumKeywordLength)
            .When(request => request.Keyword is not null);
    }

    public static string? Normalize(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
}
