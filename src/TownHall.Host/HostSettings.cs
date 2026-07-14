namespace TownHall.Host;

public class HostSettings
{
    public int? Port { get; set; }
    public bool MustRecreateDb { get; set; } = false;
}
