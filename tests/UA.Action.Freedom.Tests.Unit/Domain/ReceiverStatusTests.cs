using AwesomeAssertions;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Domain;

/// <summary>
/// A Receiver may be sent to only once an Administrator has recorded it as registered
/// (ADR 0012, D30, D35). The status says nothing about what kind of body the Receiver is (D33).
/// </summary>
public class ReceiverStatusTests
{
    [Theory]
    [InlineData(ReceiverStatus.Registered, true)]
    [InlineData(ReceiverStatus.Pending, false)]
    [InlineData(ReceiverStatus.Suspended, false)]
    [InlineData(ReceiverStatus.Expired, false)]
    public void Only_a_registered_receiver_can_be_sent_to(ReceiverStatus status, bool expected)
    {
        ReceiverRegistration.CanReceive(status).Should().Be(expected);
    }

    [Fact]
    public void A_new_receiver_starts_pending()
    {
        ReceiverRegistration.Initial.Should().Be(ReceiverStatus.Pending);
    }

    [Fact]
    public void The_status_values_are_stable_because_they_are_stored()
    {
        ((int)ReceiverStatus.Pending).Should().Be(0);
        ((int)ReceiverStatus.Registered).Should().Be(1);
        ((int)ReceiverStatus.Suspended).Should().Be(2);
        ((int)ReceiverStatus.Expired).Should().Be(3);
    }
}
