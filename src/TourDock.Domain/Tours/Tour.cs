using TourDock.Domain.Common;

namespace TourDock.Domain.Tours;

public enum TourStatus
{
    Draft,
    Published,
    Unpublished,
}

/// <summary>How travelers can book a tour. Chosen by the agency.</summary>
public enum BookingMode
{
    /// <summary>Travelers buy individual seats and share the departure.</summary>
    Shared,

    /// <summary>One group books the whole departure.</summary>
    Private,

    /// <summary>Seats can be sold; the whole departure can be booked privately while no seat is sold yet.</summary>
    Both,
}

/// <summary>Pricing that applies to a departure on a given local date (seasonal price or base price).</summary>
public sealed record TourPricing(Money? PricePerPerson, Money? PrivatePrice);

/// <summary>An offer published by an agency. Owns its pricing, seasonal prices, policy and schedule rules.</summary>
public sealed class Tour : Entity, IAuditable, ISoftDeletable
{
    public const int TitleMaxLength = 150;
    public const int DescriptionMaxLength = 5000;
    public const int MaxGuestsLimit = 500;
    public const int MaxPhotos = 20;

    private readonly List<SeasonalPrice> _seasonalPrices = [];
    private readonly List<TourPhoto> _photos = [];
    private readonly List<ScheduleRule> _scheduleRules = [];

    private Tour(Guid id, Guid agencyId)
        : base(id) => AgencyId = agencyId;

    private Tour() { }

    public Guid AgencyId { get; private set; }

    public string Title { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public Location Location { get; private set; } = null!;

    public TimeSpan Duration { get; private set; }

    public int MaxGuests { get; private set; }

    public BookingMode BookingMode { get; private set; }

    /// <summary>Base price per seat. Required for <see cref="BookingMode.Shared"/> and <see cref="BookingMode.Both"/>.</summary>
    public Money? PricePerPerson { get; private set; }

    /// <summary>Base price for the whole departure. Required for <see cref="BookingMode.Private"/> and <see cref="BookingMode.Both"/>.</summary>
    public Money? PrivatePrice { get; private set; }

    public CancellationPolicy CancellationPolicy { get; private set; } = CancellationPolicy.Default;

    public TourStatus Status { get; private set; }

    public IReadOnlyCollection<SeasonalPrice> SeasonalPrices => _seasonalPrices.AsReadOnly();

    public IReadOnlyCollection<ScheduleRule> ScheduleRules => _scheduleRules.AsReadOnly();

    /// <summary>Photos in display order; the cover photo is shown first in search results.</summary>
    public IReadOnlyList<TourPhoto> Photos => _photos.OrderBy(p => p.SortOrder).ToList();

    public TourPhoto? CoverPhoto => _photos.SingleOrDefault(p => p.IsCover);

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsBookable => Status == TourStatus.Published && !IsDeleted;

    public static Result<Tour> Create(
        Guid agencyId,
        string title,
        string description,
        Location location,
        TimeSpan duration,
        int maxGuests,
        BookingMode bookingMode,
        Money? pricePerPerson,
        Money? privatePrice,
        CancellationPolicy? cancellationPolicy = null)
    {
        var tour = new Tour(NewId(), agencyId) { Status = TourStatus.Draft };
        var result = tour.UpdateDetails(title, description, location, duration, maxGuests);
        if (result.IsFailure)
        {
            return result.Error;
        }

        result = tour.UpdatePricing(bookingMode, pricePerPerson, privatePrice);
        if (result.IsFailure)
        {
            return result.Error;
        }

        tour.CancellationPolicy = cancellationPolicy ?? CancellationPolicy.Default;
        return tour;
    }

    public Result UpdateDetails(string title, string description, Location location, TimeSpan duration, int maxGuests)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > TitleMaxLength)
        {
            return TourErrors.InvalidTitle;
        }

        if (description.Length > DescriptionMaxLength)
        {
            return TourErrors.InvalidDescription;
        }

        if (duration < TimeSpan.FromMinutes(15) || duration > TimeSpan.FromDays(14))
        {
            return TourErrors.InvalidDuration;
        }

        if (maxGuests is < 1 or > MaxGuestsLimit)
        {
            return TourErrors.InvalidMaxGuests;
        }

        Title = title.Trim();
        Description = description.Trim();
        Location = location;
        Duration = duration;
        MaxGuests = maxGuests;
        return Result.Success();
    }

    /// <remarks>Existing bookings keep the price they were made with (price snapshot on the booking).</remarks>
    public Result UpdatePricing(BookingMode mode, Money? pricePerPerson, Money? privatePrice)
    {
        var validation = ValidatePricing(mode, pricePerPerson, privatePrice);
        if (validation.IsFailure)
        {
            return validation;
        }

        BookingMode = mode;
        PricePerPerson = AllowsShared(mode) ? pricePerPerson : null;
        PrivatePrice = AllowsPrivate(mode) ? privatePrice : null;
        return Result.Success();
    }

    public void ChangeCancellationPolicy(CancellationPolicy policy) => CancellationPolicy = policy;

    public Result Publish()
    {
        if (IsDeleted)
        {
            return TourErrors.Deleted;
        }

        Status = TourStatus.Published;
        return Result.Success();
    }

    public void Unpublish() => Status = TourStatus.Unpublished;

    public void Delete(DateTimeOffset now)
    {
        IsDeleted = true;
        DeletedAt = now;
        Status = TourStatus.Unpublished;
    }

    public Result AddSeasonalPrice(DateOnly from, DateOnly to, Money? pricePerPerson, Money? privatePrice)
    {
        if (to < from)
        {
            return TourErrors.InvalidSeasonDates;
        }

        if (_seasonalPrices.Any(s => s.Overlaps(from, to)))
        {
            return TourErrors.OverlappingSeason;
        }

        // A season may override one or both prices; whatever it leaves out falls back to the base price.
        if (pricePerPerson is null && privatePrice is null)
        {
            return TourErrors.MissingPrice("A season must override at least one price.");
        }

        if (pricePerPerson?.IsZero == true || privatePrice?.IsZero == true)
        {
            return TourErrors.MissingPrice("Seasonal prices must be greater than zero.");
        }

        _seasonalPrices.Add(new SeasonalPrice(Id, from, to, pricePerPerson, privatePrice));
        return Result.Success();
    }

    public Result RemoveSeasonalPrice(Guid seasonalPriceId)
    {
        var season = _seasonalPrices.SingleOrDefault(s => s.Id == seasonalPriceId);
        if (season is null)
        {
            return TourErrors.SeasonNotFound;
        }

        _seasonalPrices.Remove(season);
        return Result.Success();
    }

    /// <summary>Prices for a departure on the given local date: the matching season if any, otherwise the base prices.</summary>
    public TourPricing PricingOn(DateOnly localDate)
    {
        var season = _seasonalPrices.FirstOrDefault(s => s.Covers(localDate));
        return season is null
            ? new TourPricing(PricePerPerson, PrivatePrice)
            : new TourPricing(season.PricePerPerson ?? PricePerPerson, season.PrivatePrice ?? PrivatePrice);
    }

    public Result<ScheduleRule> AddScheduleRule(
        Guid vehicleId, DaysOfWeek days, TimeOnly startTime, DateOnly validFrom, DateOnly? validUntil)
    {
        if (days == DaysOfWeek.None)
        {
            return TourErrors.InvalidSchedule("Select at least one day of the week.");
        }

        if (validUntil < validFrom)
        {
            return TourErrors.InvalidSchedule("The end date must not be before the start date.");
        }

        // Same vehicle, same start time, a shared weekday and overlapping dates: that rule already makes these departures.
        var duplicate = _scheduleRules.Any(r =>
            r.IsActive
            && r.VehicleId == vehicleId
            && r.StartTime == startTime
            && (r.Days & days) != DaysOfWeek.None
            && (r.ValidUntil is null || r.ValidUntil >= validFrom)
            && (validUntil is null || validUntil >= r.ValidFrom));
        if (duplicate)
        {
            return TourErrors.DuplicateScheduleRule;
        }

        var rule = new ScheduleRule(Id, vehicleId, days, startTime, validFrom, validUntil);
        _scheduleRules.Add(rule);
        return rule;
    }

    public Result DeactivateScheduleRule(Guid ruleId)
    {
        var rule = _scheduleRules.SingleOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            return TourErrors.ScheduleRuleNotFound;
        }

        rule.Deactivate();
        return Result.Success();
    }

    public Result<TourPhoto> AddPhoto(Guid photoId, string storageKey, int width, int height)
    {
        if (_photos.Count >= MaxPhotos)
        {
            return TourErrors.TooManyPhotos;
        }

        var photo = new TourPhoto(photoId, Id, storageKey, width, height, _photos.Count == 0 ? 0 : _photos.Max(p => p.SortOrder) + 1)
        {
            IsCover = _photos.Count == 0, // the first photo becomes the cover
        };
        _photos.Add(photo);
        return photo;
    }

    /// <summary>Removes a photo; if it was the cover, the next photo in order becomes the cover.</summary>
    public Result<TourPhoto> RemovePhoto(Guid photoId)
    {
        var photo = _photos.SingleOrDefault(p => p.Id == photoId);
        if (photo is null)
        {
            return TourErrors.PhotoNotFound;
        }

        _photos.Remove(photo);
        if (photo.IsCover && _photos.Count > 0)
        {
            _photos.MinBy(p => p.SortOrder)!.IsCover = true;
        }

        return photo;
    }

    public Result SetCoverPhoto(Guid photoId)
    {
        var photo = _photos.SingleOrDefault(p => p.Id == photoId);
        if (photo is null)
        {
            return TourErrors.PhotoNotFound;
        }

        foreach (var p in _photos)
        {
            p.IsCover = p.Id == photoId;
        }

        return Result.Success();
    }

    /// <summary>Sets the display order. The list must contain every photo of the tour exactly once.</summary>
    public Result ReorderPhotos(IReadOnlyList<Guid> photoIds)
    {
        if (photoIds.Count != _photos.Count || photoIds.Distinct().Count() != photoIds.Count || photoIds.Any(id => _photos.All(p => p.Id != id)))
        {
            return TourErrors.InvalidPhotoOrder;
        }

        for (var i = 0; i < photoIds.Count; i++)
        {
            _photos.Single(p => p.Id == photoIds[i]).SortOrder = i;
        }

        return Result.Success();
    }

    public static bool AllowsShared(BookingMode mode) => mode is BookingMode.Shared or BookingMode.Both;

    public static bool AllowsPrivate(BookingMode mode) => mode is BookingMode.Private or BookingMode.Both;

    private static Result ValidatePricing(BookingMode mode, Money? pricePerPerson, Money? privatePrice)
    {
        if (AllowsShared(mode) && (pricePerPerson is null || pricePerPerson.IsZero))
        {
            return TourErrors.MissingPrice("A price per person greater than zero is required for shared bookings.");
        }

        if (AllowsPrivate(mode) && (privatePrice is null || privatePrice.IsZero))
        {
            return TourErrors.MissingPrice("A private price greater than zero is required for private bookings.");
        }

        // Booking the whole trip can't be cheaper than one seat on it, or nobody would book a seat.
        if (mode == BookingMode.Both && privatePrice!.Amount < pricePerPerson!.Amount)
        {
            return TourErrors.PrivatePriceTooLow;
        }

        return Result.Success();
    }
}

public static class TourErrors
{
    public static readonly Error InvalidTitle =
        Error.Validation("Tour.InvalidTitle", $"Title is required and must be at most {Tour.TitleMaxLength} characters.");

    public static readonly Error InvalidDescription =
        Error.Validation("Tour.InvalidDescription", $"Description must be at most {Tour.DescriptionMaxLength} characters.");

    public static readonly Error InvalidDuration =
        Error.Validation("Tour.InvalidDuration", "Duration must be between 15 minutes and 14 days.");

    public static readonly Error InvalidMaxGuests =
        Error.Validation("Tour.InvalidMaxGuests", $"Max guests must be between 1 and {Tour.MaxGuestsLimit}.");

    public static readonly Error InvalidSeasonDates =
        Error.Validation("Tour.InvalidSeasonDates", "The season end date must not be before its start date.");

    public static readonly Error OverlappingSeason =
        Error.Conflict("Tour.OverlappingSeason", "The season overlaps an existing seasonal price.");

    public static readonly Error SeasonNotFound = Error.NotFound("Tour.SeasonNotFound", "Seasonal price not found.");

    public static readonly Error ScheduleRuleNotFound = Error.NotFound("Tour.ScheduleRuleNotFound", "Schedule rule not found.");

    public static readonly Error DuplicateScheduleRule = Error.Conflict(
        "Tour.DuplicateScheduleRule",
        "An active schedule already runs this vehicle at that time on some of these days. Stop it first, or pick another time or vehicle.");

    public static readonly Error NotBookable = Error.Conflict("Tour.NotBookable", "The tour is not available for booking.");

    public static readonly Error Deleted = Error.Conflict("Tour.Deleted", "The tour has been deleted.");

    public static readonly Error TooManyPhotos = Error.Conflict("Tour.TooManyPhotos", $"A tour can have at most {Tour.MaxPhotos} photos.");

    public static readonly Error PhotoNotFound = Error.NotFound("Tour.PhotoNotFound", "Photo not found.");

    public static readonly Error InvalidPhotoOrder =
        Error.Validation("Tour.InvalidPhotoOrder", "The order must list every photo of the tour exactly once.");

    public static Error MissingPrice(string message) => Error.Validation("Tour.MissingPrice", message);

    public static readonly Error PrivatePriceTooLow =
        Error.Validation("Tour.PrivatePriceTooLow", "The private trip price can't be lower than the price per person.");

    public static Error InvalidLocation(string message) => Error.Validation("Tour.InvalidLocation", message);

    public static Error InvalidCancellationPolicy(string message) => Error.Validation("Tour.InvalidCancellationPolicy", message);

    public static Error InvalidSchedule(string message) => Error.Validation("Tour.InvalidSchedule", message);
}
