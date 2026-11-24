using FluentValidation;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.TravelInfo;
using TourDock.Domain.Common;
using TourDock.Domain.Tours;

namespace TourDock.Application.Catalog;

public sealed record SearchToursQuery(
    string? City,
    string? Country,
    DateOnly? Date,
    int Guests,
    decimal? MinPrice,
    decimal? MaxPrice,
    BookingMode? Mode,
    TourSort Sort,
    int Page,
    int PageSize,
    Guid? CityId = null,
    int? RadiusKm = null,
    string? Q = null) : IQuery<TourSearchResponse>;

internal sealed class SearchToursValidator : AbstractValidator<SearchToursQuery>
{
    public const int MaxPageSize = 50;

    /// <summary>Longest free-text description of the trip (q).</summary>
    public const int MaxTextLength = 200;

    public SearchToursValidator()
    {
        RuleFor(q => q.City).MaximumLength(100);
        RuleFor(q => q.Q).MaximumLength(MaxTextLength);
        RuleFor(q => q.Country).Length(2).When(q => q.Country is not null);
        RuleFor(q => q.Guests).InclusiveBetween(1, Tour.MaxGuestsLimit);
        RuleFor(q => q.MinPrice).GreaterThanOrEqualTo(0);
        RuleFor(q => q.MaxPrice).GreaterThanOrEqualTo(q => q.MinPrice ?? 0).When(q => q.MaxPrice is not null);
        RuleFor(q => q.Mode).IsInEnum().NotEqual(BookingMode.Both).WithMessage("Mode must be Shared or Private.");
        RuleFor(q => q.Sort).IsInEnum();
        RuleFor(q => q.RadiusKm).InclusiveBetween(1, PlaceSearch.MaxRadiusKm).When(q => q.RadiusKm is not null);
        RuleFor(q => q.Page).InclusiveBetween(1, 1000);
        RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

/// <summary>
/// With free text (q), the filters still decide which tours match and the text's meaning decides their order. If the
/// text can't be embedded (no provider, provider down), the search still answers in its normal order and says so.
/// </summary>
internal sealed class SearchToursHandler(ITourCatalog catalog, IQueryEmbeddings embeddings)
    : IQueryHandler<SearchToursQuery, TourSearchResponse>
{
    public async Task<Result<TourSearchResponse>> Handle(SearchToursQuery query, CancellationToken cancellationToken)
    {
        var hasText = !string.IsNullOrWhiteSpace(query.Q);
        var semantic = hasText && await embeddings.TryEmbedAsync(query.Q!, cancellationToken) is { } vector
            ? new SemanticQuery(embeddings.Model, vector)
            : null;

        var page = await catalog.SearchAsync(query, semantic, cancellationToken);
        return new TourSearchResponse(page.Items, page.Page, page.PageSize, page.TotalCount, hasText ? semantic is not null : null);
    }
}
