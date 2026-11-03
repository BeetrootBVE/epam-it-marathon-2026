using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace RestaurantAqa;

public sealed record Session(string Id, string Token);
public sealed record ItemSnapshot(DishSnapshot Dish, int Quantity);
public sealed record DishSnapshot(string Id, string Name, decimal Price);
public sealed record ParticipantSnapshot(string? Id, string Type, string Name,
    List<ItemSnapshot> Items, decimal Total, bool IsSubmitted);
public sealed record GroupSnapshot(List<ParticipantSnapshot> Participants, decimal Total);

/// <summary>Thin Playwright REST client for the actual deployed Green & Tasty API.</summary>
public sealed class RestaurantApi : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IPlaywright _playwright;
    private readonly IAPIRequestContext _request;
    private readonly AqaConfig _config;
    private readonly List<(string reservationId, string groupId, List<string> guests)> _created = [];
    private string? _ownerToken;

    private RestaurantApi(IPlaywright playwright, IAPIRequestContext request, AqaConfig config)
    {
        _playwright = playwright;
        _request = request;
        _config = config;
    }

    public static async Task<RestaurantApi> CreateAsync(AqaConfig config)
    {
        var playwright = await Playwright.CreateAsync();
        var request = await playwright.APIRequest.NewContextAsync(new() { BaseURL = config.ApiUrl });
        return new RestaurantApi(playwright, request, config);
    }

    public async Task<string> SignInAsync(string email, string password)
    {
        var response = await _request.PostAsync("signin", new() { DataObject = new { email, password } });
        RequireStatus(response, 200, "sign in");
        using var doc = JsonDocument.Parse(await response.TextAsync());
        return StringProperty(doc.RootElement, "IdToken");
    }

    public async Task<string> SignInOwnerAsync()
    {
        _ownerToken ??= await SignInAsync(_config.OwnerEmail, _config.OwnerPassword);
        return _ownerToken;
    }

    public async Task<string> CreateGroupAsync(int guestCount = 4)
    {
        var ownerToken = await SignInOwnerAsync();
        // Disjoint future start times on a shared test table. Avoid fixed dates and near-time cancellation limits.
        var uniqueMinutes = (int)(BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0) % 120_000);
        var start = DateTime.UtcNow.AddDays(45).AddMinutes(uniqueMinutes);
        var response = await _request.PostAsync("reservations", new()
        {
            Headers = Auth(ownerToken),
            DataObject = new
            {
                tableId = _config.TableId,
                startTime = start.ToString("O"),
                endTime = start.AddHours(2).ToString("O"),
                guestCount
            }
        });
        RequireStatus(response, 201, "create test reservation (check table capacity/availability)");
        using var reservationDoc = JsonDocument.Parse(await response.TextAsync());
        var reservationId = StringProperty(reservationDoc.RootElement, "id");
        _created.Add((reservationId, string.Empty, []));

        var inviteResponse = await _request.GetAsync($"reservations/{Uri.EscapeDataString(reservationId)}/invite-token",
            new() { Headers = Auth(ownerToken) });
        RequireStatus(inviteResponse, 200, "get invite token");
        using var tokenDoc = JsonDocument.Parse(await inviteResponse.TextAsync());
        var inviteToken = StringProperty(tokenDoc.RootElement, "token");

        var groupResponse = await _request.GetAsync($"guest/invites/{Uri.EscapeDataString(inviteToken)}");
        RequireStatus(groupResponse, 200, "resolve group invitation");
        using var groupDoc = JsonDocument.Parse(await groupResponse.TextAsync());
        var groupId = StringProperty(groupDoc.RootElement, "GroupReservationId");
        _created[^1] = (reservationId, groupId, []);
        return groupId;
    }

    public async Task<Session> JoinAsync(string groupId)
    {
        var displayName = "AqaGuest-" + Guid.NewGuid().ToString("N")[..10];
        var response = await _request.PostAsync("guest/sessions", new()
        {
            DataObject = new { groupReservationId = groupId, guestName = displayName }
        });
        RequireStatus(response, 200, "guest joins group");
        using var doc = JsonDocument.Parse(await response.TextAsync());
        var session = new Session(StringProperty(doc.RootElement, "SessionId"),
                                  StringProperty(doc.RootElement, "SessionToken"));
        var itemIndex = _created.FindIndex(x => x.groupId == groupId);
        if (itemIndex >= 0) _created[itemIndex].guests.Add(session.Id);
        return session;
    }

    public async Task AddDishAsync(Session guest, int quantity)
    {
        var response = await _request.PostAsync($"guest/sessions/{Uri.EscapeDataString(guest.Id)}/dishes", new()
        {
            Headers = Auth(guest.Token), DataObject = new { dishId = _config.DishId, quantity }
        });
        RequireStatus(response, 200, "add dish to guest cart (check dish availability)");
    }

    public async Task RemoveDishAsync(Session guest, int quantity)
    {
        var response = await _request.DeleteAsync($"guest/sessions/{Uri.EscapeDataString(guest.Id)}/dishes", new()
        {
            Headers = Auth(guest.Token), DataObject = new { dishId = _config.DishId, quantity }
        });
        RequireStatus(response, 200, "remove dish from guest cart");
    }

    public async Task SubmitCartAsync(Session guest)
    {
        var response = await _request.PostAsync($"guest/sessions/{Uri.EscapeDataString(guest.Id)}/submit", new()
        {
            Headers = Auth(guest.Token)
        });
        RequireStatus(response, 200, "submit guest cart");
    }

    public async Task<IAPIResponse> GetSnapshotResponseAsync(string groupId, string? bearerToken)
        => await _request.GetAsync($"guest/groups/{Uri.EscapeDataString(groupId)}/sessions",
            bearerToken is null ? null : new() { Headers = Auth(bearerToken) });

    public async Task<GroupSnapshot> GetOwnerSnapshotAsync(string groupId)
    {
        var response = await GetSnapshotResponseAsync(groupId, await SignInOwnerAsync());
        RequireStatus(response, 200, "owner reads group snapshot");
        return JsonSerializer.Deserialize<GroupSnapshot>(await response.TextAsync(), JsonOptions)
            ?? throw new AssertionException("Group API returned empty or invalid snapshot JSON.");
    }

    private static Dictionary<string, string> Auth(string token) => new() { ["Authorization"] = "Bearer " + token };

    public static void RequireStatus(IAPIResponse response, int expected, string operation)
        => Assert.That(response.Status, Is.EqualTo(expected),
            $"Unexpected HTTP status for {operation}. Credentials/tokens and response bodies are never logged.");

    private static string StringProperty(JsonElement data, string name)
    {
        foreach (var p in data.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.GetString() ?? throw new AssertionException($"Empty '{name}' in response.");
        throw new AssertionException($"Missing '{name}' in response JSON.");
    }

    public async ValueTask DisposeAsync()
    {
        // Best-effort cleanup through EXISTING owner API, even after a failing assertion.
        // Cancellation closes the group; data may remain stored because a hard-delete API is absent.
        foreach (var created in _created.AsEnumerable().Reverse())
        {
            try
            {
                if (!string.IsNullOrEmpty(created.groupId))
                    foreach (var id in created.guests)
                    {
                        var guestDelete = await _request.DeleteAsync(
                            $"guest/groups/{Uri.EscapeDataString(created.groupId)}/sessions/{Uri.EscapeDataString(id)}",
                            new() { Headers = Auth(_ownerToken!) });
                        if (!guestDelete.Ok)
                            TestContext.Error.WriteLine($"Guest cleanup failed (HTTP {guestDelete.Status}).");
                    }
                var cancellation = await _request.DeleteAsync("reservations", new()
                {
                    Headers = Auth(_ownerToken!), DataObject = new { id = created.reservationId }
                });
                if (!cancellation.Ok)
                    TestContext.Error.WriteLine($"Reservation cleanup failed (HTTP {cancellation.Status}).");
            }
            catch
            {
                // Do not suppress the primary assertion failure; check backend for orphaned test data.
                TestContext.Error.WriteLine("AQA cleanup failed for an isolated reservation; check AWS test data manually.");
            }
        }
        await _request.DisposeAsync();
        _playwright.Dispose();
    }
}
