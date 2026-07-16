namespace TownHall;

/// <summary>
/// Generates "Adjective Animal" display names; deterministic for a given seed string. Used for a
/// new user's default name and for the per-room anonymous pseudonym (see <see cref="AnonId"/>).
/// </summary>
public static class NameGenerator
{
    private static readonly string[] Adjectives = [
        "Punctual", "Skeptical", "Radiant", "Curious", "Brave", "Gentle", "Witty", "Zesty",
        "Mellow", "Nimble", "Jolly", "Quirky", "Serene", "Bold", "Cheerful", "Dapper",
        "Eager", "Fuzzy", "Groovy", "Humble", "Inventive", "Keen", "Lively", "Merry",
        "Noble", "Optimistic", "Plucky", "Quick", "Rustic", "Snazzy", "Thoughtful", "Upbeat",
        "Vivid", "Whimsical", "Youthful", "Zippy", "Amiable", "Breezy", "Cosmic", "Daring",
    ];
    private static readonly string[] Animals = [
        "Otter", "Marmot", "Pelican", "Badger", "Capybara", "Dolphin", "Egret", "Ferret",
        "Gazelle", "Hedgehog", "Ibex", "Jackal", "Koala", "Lemur", "Meerkat", "Narwhal",
        "Ocelot", "Puffin", "Quokka", "Raccoon", "Seal", "Toucan", "Urchin", "Vulture",
        "Walrus", "Yak", "Zebra", "Alpaca", "Bison", "Chinchilla", "Dingo", "Falcon",
        "Gopher", "Heron", "Iguana", "Kiwi", "Lynx", "Mongoose", "Newt", "Osprey",
    ];

    public static string New(string seed)
    {
        var hash = seed.GetXxHash3L();
        var adjective = Adjectives[(int)(hash % (ulong)Adjectives.Length)];
        var animal = Animals[(int)((hash >> 16) % (ulong)Animals.Length)];
        return $"{adjective} {animal}";
    }
}
