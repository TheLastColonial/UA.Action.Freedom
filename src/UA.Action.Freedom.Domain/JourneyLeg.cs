namespace UA.Action.Freedom.Domain;

/// <summary>
/// Which half of the journey a crew is driving.
/// </summary>
/// <remarks>
/// A leg is a property of the <see cref="Convoy"/>'s journey, not of the <see cref="Manifest"/>.
/// It lives on the crew row (<see cref="CrewRole"/> says what a person does; this says when), so
/// that a handover at the European border is recordable: one crew takes the vehicle out of the UK,
/// another takes it into Ukraine. It was called <c>ManifestLeg</c> while the manifest carried its
/// own driver teams, which is exactly the confusion this rename removes.
/// </remarks>
public enum JourneyLeg
{
    /// <summary>UK to Europe.</summary>
    Uk = 0,

    /// <summary>Europe to Ukraine.</summary>
    Border = 1,
}
