namespace UA.Action.Freedom.Domain;

/// <summary>
/// Which way a transport unit crosses the Channel, named from the French border's side.
/// </summary>
/// <remarks>
/// UK to France is <see cref="Import"/>, however much it feels like an export from Dover. Version
/// 1.1.0 of the ELO service contract renamed these from ENTREE/SORTIE, and the new names read as
/// the shipper's point of view without being it.
/// </remarks>
public enum EloCrossingDirection
{
    Import,
    Export,
}

/// <summary>
/// Whether the transport unit carries goods. French customs calls this
/// <c>type_unite_transport</c>.
/// </summary>
public enum EloLorryType
{
    Empty,
    Loaded,
}

/// <summary>
/// The pairing information declared on an ELO envelope — what French customs needs to know about a
/// crossing in order to decide which formalities the envelope must contain.
/// </summary>
/// <remarks>
/// This is the whole of what an ELO says about a lorry. It carries no goods description, no
/// weights, no consignor or consignee, no addresses and not even a registration: those live in the
/// underlying customs declarations, which the envelope only references. That is why an ELO request
/// cannot leak a Ukrainian delivery address — there is nowhere to put one.
///
/// <para>
/// The flags are not independent. Cross-functional rule ENV_CTR_RG08 makes the required set of
/// declarations a function of <see cref="Direction"/>, <see cref="LorryType"/> and
/// <see cref="TirAta"/> together, which is why <see cref="RequiresADeclaration"/> lives here rather
/// than in whichever caller happens to build the request.
/// </para>
/// </remarks>
/// <param name="Direction">Which way the transport unit is crossing.</param>
/// <param name="LorryType">Whether it carries goods.</param>
/// <param name="TirAta">Whether TIR or ATA formalities apply.</param>
/// <param name="HasTransportContract">
/// Whether a transport contract exists. French customs accepts this as true only for a UK to France
/// crossing, and only for a TIR/ATA lorry carrying empty packaging or pallets.
/// </param>
/// <param name="Postal">Whether the unit carries postal goods. UK to France only.</param>
/// <param name="EmptyPackaging">
/// Whether the unit carries empty packaging or pallets. UK to France only.
/// </param>
/// <param name="SanitaryOrPhytosanitary">
/// Whether the unit carries goods subject to sanitary or phytosanitary control.
/// </param>
/// <param name="FisheryProducts">Whether the unit carries fishery products.</param>
public sealed record EloCrossingProfile(
    EloCrossingDirection Direction,
    EloLorryType LorryType,
    bool TirAta,
    bool HasTransportContract,
    bool Postal,
    bool EmptyPackaging,
    bool SanitaryOrPhytosanitary,
    bool FisheryProducts)
{
    /// <summary>
    /// How Ukrainian Action's convoys cross: a loaded lorry arriving in France under TIR/ATA with
    /// no transport contract, carrying nothing that pulls in a further border regime.
    /// </summary>
    /// <remarks>
    /// The TIR/ATA choice is French Customs' own guidance for this traffic, recorded in issue #24:
    /// "For emergency humanitarian aid destined for Ukraine, select TIR/ATA without transport
    /// contract." It is load-bearing rather than cosmetic. Under ENV_CTR_RG08 a loaded lorry
    /// <em>outside</em> TIR/ATA arriving in France must present an ENS <em>and</em> a
    /// customs-clearance formality; under TIR/ATA the ENS alone suffices. Setting
    /// <see cref="TirAta"/> to false therefore doubles what a dispatcher must obtain before a
    /// convoy can sail.
    /// </remarks>
    public static EloCrossingProfile HumanitarianAidToUkraine { get; } = new(
        Direction: EloCrossingDirection.Import,
        LorryType: EloLorryType.Loaded,
        TirAta: true,
        HasTransportContract: false,
        Postal: false,
        EmptyPackaging: false,
        SanitaryOrPhytosanitary: false,
        FisheryProducts: false);

    /// <summary>
    /// Whether the envelope must name at least one declaration.
    /// </summary>
    /// <remarks>
    /// ENV_CTR_RG08: an empty transport unit must carry no formalities at all, and a loaded one
    /// always carries at least one. Submitting an envelope that breaks this is refused with
    /// FONC-ERR-004 rather than accepted and corrected, so it is worth asking before the call.
    /// </remarks>
    public bool RequiresADeclaration => this.LorryType == EloLorryType.Loaded;
}
