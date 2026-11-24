using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TourDock.Application.Abstractions;
using TourDock.Application.Abstractions.Messaging;
using TourDock.Application.Catalog;
using TourDock.Application.Photos;
using TourDock.Application.Slots;
using TourDock.Domain.Agencies;
using TourDock.Domain.Common;
using TourDock.Domain.Slots;
using TourDock.Domain.Tours;
using TourDock.Domain.Vehicles;
using TourDock.Infrastructure.Identity;
using TourDock.Infrastructure.Persistence;

namespace TourDock.Infrastructure.Seeding;

/// <summary>
/// Keeps the fictional demo catalog (<see cref="DemoCatalog"/>) in the database so the app is explorable right after
/// `docker compose up`. Enabled with Seed__DemoData=true.
/// <para>
/// On every start it brings the demo agencies' tours to the catalog's state: creates missing agencies, vehicles and tours,
/// updates texts, prices, refund policies, seasonal prices and schedules, and retires tours the catalog no longer offers
/// (soft delete, so past bookings and reviews keep their tour). Departures of replaced schedules are cancelled only where
/// nobody has booked. When nothing differs, nothing is written.
/// </para>
/// <para>
/// Photos are uploaded afterwards in the background through the normal image pipeline (validation, WebP variants, no
/// metadata), only for demo tours without photos, so startup and health checks aren't held up by image processing.
/// </para>
/// </summary>
internal sealed partial class DemoDataSeeder(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<DemoDataSeeder> logger)
    : IHostedService, IDisposable
{
    /// <summary>Valid-from date of year-round schedules; fixed so the rule doesn't change from one start to the next.</summary>
    private static readonly DateOnly YearRoundStart = new(2026, 1, 1);

    private readonly CancellationTokenSource _stopping = new();

    /// <summary>The background photo upload of the last start (exposed for tests).</summary>
    internal Task PhotoSeeding { get; private set; } = Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Seed:DemoData", false))
        {
            return;
        }

        await SyncCatalogAsync(cancellationToken);
        PhotoSeeding = Task.Run(() => SeedPhotosAsync(_stopping.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _stopping.CancelAsync();
            await PhotoSeeding.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting down (unfinished photos follow on the next start), or the host already disposed this service.
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task SyncCatalogAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TourDockDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var specs = DemoCatalog.Agencies();
        var emails = specs.Select(s => s.OwnerEmail).ToList();
        var agencies = await db.Agencies.Where(a => emails.Contains(a.ContactEmail)).ToDictionaryAsync(a => a.ContactEmail, cancellationToken);
        var agencyIds = agencies.Values.Select(a => a.Id).ToList();
        var vehicles = await db.Vehicles.Where(v => agencyIds.Contains(v.AgencyId)).ToListAsync(cancellationToken);
        var tours = await db.Tours
            .Include(t => t.SeasonalPrices)
            .Include(t => t.ScheduleRules)
            .Where(t => agencyIds.Contains(t.AgencyId) && !t.IsDeleted)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var replacedRules = new List<Guid>();
        foreach (var spec in specs)
        {
            if (!agencies.TryGetValue(spec.OwnerEmail, out var agency))
            {
                var owner = await GetOrCreateOwnerAsync(users, spec.OwnerEmail, spec.OwnerName);
                agency = Agency.Register(spec.Name, spec.OwnerEmail, owner).Value;
                agency.Approve();
                db.Agencies.Add(agency);
            }

            var fleet = new Dictionary<string, Vehicle>();
            foreach (var v in spec.Vehicles)
            {
                var vehicle = vehicles.FirstOrDefault(x => x.AgencyId == agency.Id && x.Name == v.Name);
                if (vehicle is null)
                {
                    vehicle = Vehicle.Create(agency.Id, v.Name, v.Type, v.Capacity).Value;
                    db.Vehicles.Add(vehicle);
                }

                fleet[v.Name] = vehicle;
            }

            foreach (var t in spec.Tours)
            {
                var tour = tours.FirstOrDefault(x => x.AgencyId == agency.Id && x.Title == t.Title);
                if (tour is null)
                {
                    tour = Tour.Create(agency.Id, t.Title, t.Description, ToLocation(t.Place), TimeSpan.FromHours(t.Hours), t.MaxGuests,
                        t.Mode, Eur(t.PerPerson), Eur(t.Private)).Value;
                    db.Tours.Add(tour);
                }

                Apply(tour, t, fleet, today, replacedRules);
            }

            foreach (var title in spec.RetiredTours)
            {
                var tour = tours.FirstOrDefault(x => x.AgencyId == agency.Id && x.Title == title);
                if (tour is not null)
                {
                    await RetireAsync(db, tour, now, replacedRules, cancellationToken);
                }
            }
        }

        if (replacedRules.Count > 0)
        {
            var orphaned = await db.Slots
                .Where(s => s.ScheduleRuleId != null && replacedRules.Contains(s.ScheduleRuleId.Value) && s.StartsAt > now
                    && s.Status != SlotStatus.Cancelled && s.ReservedSeats == 0 && !s.IsPrivatelyBooked)
                .ToListAsync(cancellationToken);
            foreach (var slot in orphaned)
            {
                slot.Cancel(now);
            }
        }

        if (!db.ChangeTracker.HasChanges())
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);

        var departures = 0;
        try
        {
            var generate = services.GetRequiredService<ICommandHandler<GenerateSlotsCommand, GenerateSlotsResult>>();
            var generated = await generate.Handle(new GenerateSlotsCommand(), cancellationToken);
            departures = generated.IsSuccess ? generated.Value.DeparturesCreated : 0;
        }
        catch (DbUpdateException)
        {
            // The slot generation job ran at the same moment; it generates the departures instead (idempotent).
        }

        await services.GetRequiredService<ICatalogCache>().InvalidateAsync(cancellationToken);
        LogSynced(logger, departures);
    }

    /// <summary>Brings one tour to its catalog state, touching only what differs.</summary>
    private static void Apply(Tour tour, DemoTour spec, Dictionary<string, Vehicle> fleet, DateOnly today, List<Guid> replacedRules)
    {
        var location = ToLocation(spec.Place);
        var duration = TimeSpan.FromHours(spec.Hours);
        var description = spec.Description.Trim();
        if (tour.Description != description || tour.Location != location || tour.Duration != duration || tour.MaxGuests != spec.MaxGuests)
        {
            Ensure(tour.UpdateDetails(spec.Title, description, location, duration, spec.MaxGuests));
        }

        if (tour.BookingMode != spec.Mode || tour.PricePerPerson != Eur(spec.PerPerson) || tour.PrivatePrice != Eur(spec.Private))
        {
            Ensure(tour.UpdatePricing(spec.Mode, Eur(spec.PerPerson), Eur(spec.Private)));
        }

        var policy = CancellationPolicy.Create(spec.Refunds).Value;
        if (tour.CancellationPolicy != policy)
        {
            tour.ChangeCancellationPolicy(policy);
        }

        var seasons = spec.Prices
            .SelectMany(p => Windows(p.FromMonth, p.FromDay, p.ToMonth, p.ToDay, today)
                .Select(w => (w.From, To: w.Until!.Value, PerPerson: Adjust(spec.PerPerson, p.Percent), Private: Adjust(spec.Private, p.Percent))))
            .ToList();
        var current = tour.SeasonalPrices.Select(s => (s.From, s.To, PerPerson: s.PricePerPerson, Private: s.PrivatePrice)).ToList();
        if (!current.ToHashSet().SetEquals(seasons))
        {
            foreach (var season in tour.SeasonalPrices.ToList())
            {
                tour.RemoveSeasonalPrice(season.Id);
            }

            foreach (var (from, to, perPerson, privatePrice) in seasons)
            {
                Ensure(tour.AddSeasonalPrice(from, to, perPerson, privatePrice));
            }
        }

        var wanted = spec.Schedule
            .SelectMany(d => ScheduleWindows(d.Season, today).Select(w => (fleet[spec.Vehicle].Id, d.Days, d.Time, w.From, w.Until)))
            .ToList();
        foreach (var rule in tour.ScheduleRules.Where(r => r.IsActive).ToList())
        {
            if (!wanted.Remove((rule.VehicleId, rule.Days, rule.StartTime, rule.ValidFrom, rule.ValidUntil)))
            {
                tour.DeactivateScheduleRule(rule.Id);
                replacedRules.Add(rule.Id);
            }
        }

        foreach (var (vehicleId, days, time, from, until) in wanted)
        {
            Ensure(tour.AddScheduleRule(vehicleId, days, time, from, until));
        }

        if (tour.Status != TourStatus.Published)
        {
            Ensure(tour.Publish());
        }
    }

    /// <summary>
    /// Takes a tour out of the catalog. A tour with upcoming reservations is only unpublished, so those trips still happen;
    /// otherwise it is soft-deleted and its schedule stopped.
    /// </summary>
    private static async Task RetireAsync(TourDockDbContext db, Tour tour, DateTimeOffset now, List<Guid> replacedRules, CancellationToken cancellationToken)
    {
        var hasReservations = await db.Slots.AnyAsync(
            s => s.TourId == tour.Id && s.StartsAt > now && s.Status != SlotStatus.Cancelled && (s.ReservedSeats > 0 || s.IsPrivatelyBooked),
            cancellationToken);
        if (hasReservations)
        {
            if (tour.Status == TourStatus.Published)
            {
                tour.Unpublish();
            }

            return;
        }

        foreach (var rule in tour.ScheduleRules.Where(r => r.IsActive).ToList())
        {
            tour.DeactivateScheduleRule(rule.Id);
            replacedRules.Add(rule.Id);
        }

        tour.Delete(now);
    }

    private async Task SeedPhotosAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<TourDockDbContext>();
            var images = services.GetRequiredService<IImageProcessor>();
            var storage = services.GetRequiredService<IFileStorage>();

            var specs = DemoCatalog.Agencies();
            var emails = specs.Select(s => s.OwnerEmail).ToList();
            var agencies = await db.Agencies.Where(a => emails.Contains(a.ContactEmail))
                .ToDictionaryAsync(a => a.Id, a => a.ContactEmail, cancellationToken);
            var agencyIds = agencies.Keys.ToList();
            var withoutPhotos = await db.Tours.Include(t => t.Photos)
                .Where(t => agencyIds.Contains(t.AgencyId) && !t.IsDeleted && !t.Photos.Any())
                .ToListAsync(cancellationToken);

            var assembly = typeof(DemoDataSeeder).Assembly;
            var seeded = 0;
            foreach (var tour in withoutPhotos)
            {
                var spec = specs.Single(s => s.OwnerEmail == agencies[tour.AgencyId]).Tours.FirstOrDefault(t => t.Title == tour.Title);
                if (spec is null)
                {
                    continue;
                }

                for (var n = 1; ; n++)
                {
                    await using var file = assembly.GetManifestResourceStream($"DemoPhotos/{spec.PhotoKey}-{n}.jpg");
                    if (file is null)
                    {
                        break;
                    }

                    var processed = images.Process(file);
                    if (processed.IsFailure)
                    {
                        LogPhotoRejected(logger, spec.PhotoKey, n, processed.Error.Message);
                        continue;
                    }

                    var photoId = Guid.CreateVersion7();
                    var storageKey = $"tours/{tour.Id}/{photoId:N}";
                    foreach (var variant in processed.Value.Variants)
                    {
                        await storage.PutAsync(PhotoVariants.Key(storageKey, variant.Name), variant.Data, "image/webp", cancellationToken);
                    }

                    var largest = processed.Value.Largest;
                    Ensure(tour.AddPhoto(photoId, storageKey, largest.Width, largest.Height));
                }

                await db.SaveChangesAsync(cancellationToken);
                seeded++;
            }

            if (seeded > 0)
            {
                await services.GetRequiredService<ICatalogCache>().InvalidateAsync(cancellationToken);
                LogPhotosSeeded(logger, seeded);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping; the next start continues with the tours that still have no photos.
        }
        catch (Exception ex)
        {
            // Storage may be down or not configured; the catalog works without photos and the next start tries again.
            LogPhotosFailed(logger, ex);
        }
    }

    /// <summary>The yearly window in each of last, this and next year that hasn't ended yet.</summary>
    private static IEnumerable<(DateOnly From, DateOnly? Until)> Windows(int fromMonth, int fromDay, int toMonth, int toDay, DateOnly today)
    {
        for (var year = today.Year - 1; year <= today.Year + 1; year++)
        {
            var from = new DateOnly(year, fromMonth, fromDay);
            var until = new DateOnly(toMonth < fromMonth ? year + 1 : year, toMonth, toDay);
            if (until >= today)
            {
                yield return (from, until);
            }
        }
    }

    private static IEnumerable<(DateOnly From, DateOnly? Until)> ScheduleWindows(Season season, DateOnly today) => season switch
    {
        Season.YearRound => [(YearRoundStart, null)],
        Season.Long => Windows(4, 1, 10, 31, today),
        Season.Summer => Windows(5, 1, 10, 15, today),
        Season.Alpine => Windows(6, 15, 9, 30, today),
        Season.OffSeason => Windows(11, 1, 3, 31, today),
        Season.Winter => Windows(12, 15, 3, 20, today),
        Season.Aurora => Windows(9, 15, 3, 31, today),
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, null),
    };

    private static Money? Adjust(decimal? price, int percent) =>
        price is { } p ? Money.Eur(Math.Round(p * (100 + percent) / 100m, MidpointRounding.AwayFromZero)) : null;

    /// <summary>The catalog is static data, so a rejected change is a bug in <see cref="DemoCatalog"/>.</summary>
    private static void Ensure(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Invalid demo catalog entry: {result.Error.Code} {result.Error.Message}");
        }
    }

    private static Money? Eur(decimal? amount) => amount is { } a ? Money.Eur(a) : null;

    private static Location ToLocation(DemoPlace p) => Location.Create(p.Country, p.City, p.MeetingPoint, p.Lat, p.Lng, p.TimeZone).Value;

    private static async Task<Guid> GetOrCreateOwnerAsync(UserManager<ApplicationUser> users, string email, string name)
    {
        if (await users.FindByEmailAsync(email) is { } existing)
        {
            return existing.Id;
        }

        var user = new ApplicationUser { Id = Guid.CreateVersion7(), Email = email, UserName = email, DisplayName = name, EmailConfirmed = true };

        // Demo owners get a random password nobody knows; they exist only so the agencies have a manager.
        var result = await users.CreateAsync(user, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
        return result.Succeeded ? user.Id : throw new InvalidOperationException("Could not create demo user.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo catalog updated ({Departures} departures generated)")]
    private static partial void LogSynced(ILogger logger, int departures);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo photos uploaded for {Tours} tours")]
    private static partial void LogPhotosSeeded(ILogger logger, int tours);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo photo {Key}-{Number} was rejected: {Reason}")]
    private static partial void LogPhotoRejected(ILogger logger, string key, int number, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo photos could not be uploaded; will retry on the next start")]
    private static partial void LogPhotosFailed(ILogger logger, Exception exception);
}
