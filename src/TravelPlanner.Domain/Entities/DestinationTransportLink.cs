using TravelPlanner.Domain.Common;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Domain.Entities;

/// <summary>Curated official transport information (airport site, operator, platform). Never generated.</summary>
public class DestinationTransportLink : Entity
{
    private DestinationTransportLink() { }

    public DestinationTransportLink(TransportLinkKind kind, string name, string url)
    {
        Kind = kind;
        Name = name;
        Url = url;
    }

    public Guid DestinationId { get; set; }
    public TransportLinkKind Kind { get; set; }
    public string Name { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsVerified { get; set; }
    public DateOnly? VerifiedOn { get; set; }
    /// <summary>Link name per language code (e.g. "ka").</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public string NameIn(string language) => Localized.Pick(NameTranslations, language, Name);
}
