using TownHall.Db;

namespace TownHall.Host;

public class HostSettings
{
    public int? Port { get; set; }
    public bool MustRecreateDb { get; set; } = false;
    public string PostgreSql { get; set; } = AppDbContext.DefaultConnectionString;

    // Passkey / WebAuthn. RpId must be the registrable domain (no scheme/port); the browser requires
    // it to match the page's origin. Origins are the full origins allowed to complete a ceremony.
    // localhost is a WebAuthn secure context over http, so the defaults work for local dev as-is.
    public string PasskeyRpId { get; set; } = "localhost";
    public string[] PasskeyOrigins { get; set; } = ["http://localhost:5136", "https://localhost:5136"];

    // Development-only convenience sign-in (no passkey). Never enable outside Development.
    public bool EnableDevSignIn { get; set; } = false;
}
