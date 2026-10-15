namespace GymApp.Core;

/// <summary>The kind of OpenStreetMap object a city or gym came from. OSM ids are only unique per type.
/// Stored as its name in the database, so don't rename the members.</summary>
public enum OsmElementType { Node, Way, Relation }
