using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace TourDock.Api.IntegrationTests;

/// <summary>Small HTTP helper so tests read like API usage.</summary>
internal sealed class TestApi(HttpClient client, CancellationToken ct)
{
    public const string Password = "correct-horse-battery";

    /// <summary>Runs after every helper sign-up with the new email, e.g. to open the confirmation link when the API requires it.</summary>
    public Func<string, Task>? OnRegistered { get; init; }

    /// <summary>Signs up, runs <see cref="OnRegistered"/> and signs in; returns the access token.</summary>
    /// <param name="runHook">False to skip the hook (only works where unconfirmed accounts may sign in).</param>
    public async Task<string> RegisterUserAsync(bool runHook = true)
    {
        var email = await SignUpAsync();
        if (runHook && OnRegistered is not null)
        {
            await OnRegistered(email);
        }

        return await LoginAsync(email);
    }

    /// <summary>Only the sign-up request (always 202); returns the email.</summary>
    public async Task<string> SignUpAsync(string? email = null, string password = Password)
    {
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var (status, _) = await SendAsync(HttpMethod.Post, "/api/v1/auth/register", new { email, password, displayName = "Test User" });
        status.Should().Be(HttpStatusCode.Accepted);
        return email;
    }

    public async Task<string> LoginAsync(string email, string password = Password)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password });
        status.Should().Be(HttpStatusCode.OK);
        return body.GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    public async Task<string> AdminTokenAsync()
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/api/v1/auth/login",
            new { email = TourDockApiFactory.AdminEmail, password = TourDockApiFactory.AdminPassword });
        status.Should().Be(HttpStatusCode.OK);
        return body.GetProperty("tokens").GetProperty("accessToken").GetString()!;
    }

    /// <summary>Creates an approved agency with one published tour and one departure; returns ids for booking tests.</summary>
    public async Task<Departure> CreateDepartureAsync(
        int capacity = 8, int daysAhead = 10, string bookingMode = "Both", string title = "Sunset Cruise", string description = "Test tour.")
    {
        var (token, agencyId) = await CreateAgencyAsync();
        var vehicleId = await CreateVehicleAsync(token, agencyId, capacity);
        var payload = new
        {
            title,
            description,
            location = new { countryCode = "HR", city = "Split", address = "Riva 1", latitude = 43.508, longitude = 16.440, timeZoneId = "Europe/Zagreb" },
            durationMinutes = 180,
            maxGuests = 10,
            bookingMode,
            pricePerPerson = bookingMode == "Private" ? (decimal?)null : 50m,
            privatePrice = bookingMode == "Shared" ? (decimal?)null : 400m,
        };
        var (_, tour) = await SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours", payload, token);
        var tourId = tour.GetProperty("id").GetString()!;
        await SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/publish", null, token);
        var (status, slot) = await SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours/{tourId}/slots",
            new { vehicleId, localStart = DateTime.UtcNow.Date.AddDays(daysAhead).AddHours(9) }, token);
        status.Should().Be(HttpStatusCode.Created);
        return new Departure(token, agencyId, tourId, slot.GetProperty("id").GetString()!);
    }

    public Task<(HttpStatusCode Status, JsonElement Body)> BookAsync(string token, string departureId, int guests, string type = "Shared", string? key = null) =>
        SendAsync(HttpMethod.Post, "/api/v1/bookings", new { departureId, type, guests }, token,
            new Dictionary<string, string> { ["Idempotency-Key"] = key ?? Guid.NewGuid().ToString() });

    public async Task<int> RemainingSeatsAsync(Departure d)
    {
        var (_, slots) = await SendAsync(HttpMethod.Get,
            $"/api/v1/agencies/{d.AgencyId}/slots?from={DateTime.UtcNow.AddDays(-1):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(60):yyyy-MM-dd}", null, d.AgencyToken);
        return slots.EnumerateArray().Single(s => s.GetProperty("id").GetString() == d.DepartureId).GetProperty("remainingSeats").GetInt32();
    }

    /// <summary>Registers a user, creates an agency for them and (optionally) has the admin approve it.</summary>
    public async Task<(string Token, string AgencyId)> CreateAgencyAsync(bool approve = true)
    {
        var token = await RegisterUserAsync();
        var (status, agency) = await SendAsync(HttpMethod.Post, "/api/v1/agencies",
            new { name = "Blue Coast Charters", contactEmail = "hello@example.com" }, token);
        status.Should().Be(HttpStatusCode.Created);
        var agencyId = agency.GetProperty("id").GetString()!;

        if (approve)
        {
            (await SendAsync(HttpMethod.Post, $"/api/v1/admin/agencies/{agencyId}/approve", null, await AdminTokenAsync()))
                .Status.Should().Be(HttpStatusCode.OK);
        }

        return (token, agencyId);
    }

    public async Task<string> CreateVehicleAsync(string token, string agencyId, int capacity = 8)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/vehicles",
            new { name = "Sea Breeze", type = "Boat", capacity }, token);
        status.Should().Be(HttpStatusCode.Created);
        return body.GetProperty("id").GetString()!;
    }

    public async Task<string> CreateTourAsync(string token, string agencyId, decimal pricePerPerson = 49.99m)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, $"/api/v1/agencies/{agencyId}/tours", TourPayload(pricePerPerson), token);
        status.Should().Be(HttpStatusCode.Created);
        return body.GetProperty("id").GetString()!;
    }

    public static object TourPayload(decimal? pricePerPerson = 49.99m) => new
    {
        title = "Sunset Cruise",
        description = "Three hours along the coast.",
        location = new { countryCode = "HR", city = "Split", address = "Riva 1", latitude = 43.508, longitude = 16.440, timeZoneId = "Europe/Zagreb" },
        durationMinutes = 180,
        maxGuests = 10,
        bookingMode = "Both",
        pricePerPerson,
        privatePrice = 450m,
    };

    public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        HttpMethod method, string url, object? payload = null, string? token = null, IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }
}

internal sealed record Departure(string AgencyToken, string AgencyId, string TourId, string DepartureId);
