using NUnit.Framework;

namespace RestaurantAqa;

[TestFixture]
[Category("API")]
[Category("L1")]
public class GroupPreOrderLevel1Tests
{
    private AqaConfig _config = null!;
    private RestaurantApi _api = null!;

    [SetUp]
    public async Task SetUp()
    {
        _config = new AqaConfig();
        _api = await RestaurantApi.CreateAsync(_config);
    }

    [TearDown]
    public async Task TearDown() => await _api.DisposeAsync();

    [Test]
    public async Task Owner_sees_guests_dishes_quantities_subtotals_and_grand_total()
    {
        var groupId = await _api.CreateGroupAsync(4);
        var first = await _api.JoinAsync(groupId);
        var second = await _api.JoinAsync(groupId);
        await _api.AddDishAsync(first, 2);
        await _api.AddDishAsync(second, 1);
        await _api.SubmitCartAsync(first);

        var snapshot = await _api.GetOwnerSnapshotAsync(groupId);
        var firstParticipant = snapshot.Participants.Single(p => p.Id == first.Id);
        var secondParticipant = snapshot.Participants.Single(p => p.Id == second.Id);

        Assert.Multiple(() =>
        {
            Assert.That(firstParticipant.Type, Is.EqualTo("Guest"));
            Assert.That(firstParticipant.Items.Single().Quantity, Is.EqualTo(2));
            Assert.That(secondParticipant.Items.Single().Quantity, Is.EqualTo(1));
            Assert.That(firstParticipant.IsSubmitted, Is.True);
            Assert.That(secondParticipant.IsSubmitted, Is.False);
            Assert.That(firstParticipant.Total, Is.EqualTo(2 * firstParticipant.Items.Single().Dish.Price));
            Assert.That(secondParticipant.Total, Is.EqualTo(secondParticipant.Items.Single().Dish.Price));
            Assert.That(snapshot.Total, Is.EqualTo(snapshot.Participants.Sum(p => p.Total)));
        });
    }

    [Test]
    public async Task Group_total_updates_when_guest_changes_the_cart()
    {
        var groupId = await _api.CreateGroupAsync(3);
        var guest = await _api.JoinAsync(groupId);
        await _api.AddDishAsync(guest, 2);
        var before = await _api.GetOwnerSnapshotAsync(groupId);
        var price = before.Participants.Single(p => p.Id == guest.Id).Items.Single().Dish.Price;

        await _api.RemoveDishAsync(guest, 1);
        var after = await _api.GetOwnerSnapshotAsync(groupId);
        Assert.Multiple(() =>
        {
            Assert.That(after.Participants.Single(p => p.Id == guest.Id).Items.Single().Quantity, Is.EqualTo(1));
            Assert.That(after.Total, Is.EqualTo(before.Total - price));
            Assert.That(after.Total, Is.EqualTo(after.Participants.Sum(p => p.Total)));
        });
    }

    [Test]
    public async Task Unsubmitted_cart_and_empty_cart_both_remain_in_participants()
    {
        var groupId = await _api.CreateGroupAsync(4);
        var pending = await _api.JoinAsync(groupId);
        var empty = await _api.JoinAsync(groupId);
        await _api.AddDishAsync(pending, 1);
        var snapshot = await _api.GetOwnerSnapshotAsync(groupId);
        var pendingParticipant = snapshot.Participants.Single(p => p.Id == pending.Id);
        var emptyParticipant = snapshot.Participants.Single(p => p.Id == empty.Id);

        Assert.Multiple(() =>
        {
            Assert.That(pendingParticipant.IsSubmitted, Is.False);
            Assert.That(pendingParticipant.Items, Has.Count.EqualTo(1));
            Assert.That(emptyParticipant.IsSubmitted, Is.False);
            Assert.That(emptyParticipant.Items, Is.Empty);
            Assert.That(emptyParticipant.Total, Is.Zero);
            Assert.That(snapshot.Total, Is.EqualTo(snapshot.Participants.Sum(p => p.Total)));
        });
    }

    [Test]
    public async Task Group_without_guests_has_no_participants_and_zero_total()
    {
        var groupId = await _api.CreateGroupAsync(2);
        var snapshot = await _api.GetOwnerSnapshotAsync(groupId);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Participants, Is.Empty);
            Assert.That(snapshot.Total, Is.Zero);
        });
    }

    [TestCase("another-customer")]
    [TestCase("waiter")]
    [TestCase("guest")]
    [TestCase("anonymous")]
    public async Task Only_group_owner_can_read_the_snapshot(string identity)
    {
        var groupId = await _api.CreateGroupAsync(3);
        string? token = identity switch
        {
            "another-customer" => await _api.SignInAsync(_config.OtherEmail, _config.OtherPassword),
            "waiter" => await _api.SignInAsync(_config.WaiterEmail, _config.WaiterPassword),
            "guest" => (await _api.JoinAsync(groupId)).Token,
            "anonymous" => null,
            _ => throw new AssertionException("Unknown role")
        };
        var response = await _api.GetSnapshotResponseAsync(groupId, token);
        // Gateway-generated unauthenticated failure may be 401 or 403; neither grants access.
        if (identity is "another-customer" or "waiter")
            Assert.That(response.Status, Is.EqualTo(403), "A different Cognito principal must not access Owner data.");
        else
            Assert.That(response.Status, Is.AnyOf(401, 403), "Guest token/anonymous request must not access Owner data.");
    }

    [Test]
    public async Task Unknown_group_returns_404_for_authenticated_owner()
    {
        var response = await _api.GetSnapshotResponseAsync(Guid.NewGuid().ToString(), await _api.SignInOwnerAsync());
        Assert.That(response.Status, Is.EqualTo(404));
    }
}
