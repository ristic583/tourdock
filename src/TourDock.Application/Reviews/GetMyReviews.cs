using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Domain.Administration;
using TourDock.Domain.Common;

namespace TourDock.Application.Reviews;

public sealed record GetMyReviewsQuery : IQuery<IReadOnlyList<MyReview>>;

internal sealed class GetMyReviewsHandler(
    IUserContext user, IReviewReader reader)
    : IQueryHandler<GetMyReviewsQuery, IReadOnlyList<MyReview>>
{
    public async Task<Result<IReadOnlyList<MyReview>>> Handle(GetMyReviewsQuery query, CancellationToken cancellationToken) =>
        Result.Success(await reader.GetForCustomerAsync(user.UserId, cancellationToken));
}
