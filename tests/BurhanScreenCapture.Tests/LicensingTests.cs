using BurhanScreenCapture.Licensing;
using Xunit;

public class LicensingTests
{
    private static readonly (string Pub, string Prv) Keys = LicenseCrypto.CreateKeyPair();
    private const string Device = "ABCD-1234-EF56-7890";

    private static LicenseData NewData(string? device = null) => new()
    {
        LicenseIdBytes = LicenseData.NewLicenseIdBytes(),
        IssuedUtc = DateTime.UtcNow,
        BoundDeviceId = device
    };

    private static string Issue(string? device = null) => LicenseKey.Create(NewData(device), Keys.Prv);
    private static LicenseStatus Eval(string? key, string device = Device, ICollection<string>? revoked = null) =>
        LicenseEvaluator.Evaluate(key, new[] { Keys.Pub }, device, revoked);

    [Fact]
    public void NoKey_IsFree()
    {
        var st = Eval(null);
        Assert.Equal(Edition.Free, st.Edition);
        Assert.False(st.IsPro);
    }

    [Fact]
    public void UnboundKey_ActivatesLifetimePro_OnAnyDevice()
    {
        var key = Issue();
        Assert.StartsWith("BNX-SC-PRO-", key);
        Assert.Equal(LicenseState.Active, Eval(key).State);
        Assert.Equal(Edition.LifetimePro, Eval(key, "1111-2222-3333-4444").Edition);
    }

    [Fact]
    public void BoundKey_OnlyActivatesOnItsDevice()
    {
        var key = Issue(Device);
        var ok = Eval(key);
        Assert.Equal(LicenseState.Active, ok.State);
        Assert.Equal(Device, ok.Data!.BoundDeviceId);
        Assert.Equal(LicenseState.WrongDevice, Eval(key, "FFFF-0000-FFFF-0000").State);
    }

    [Fact]
    public void PastedKey_WithSpacesLowercaseAndLineBreaks_IsAccepted()
    {
        var key = Issue(Device);
        var messy = "  " + key.ToLowerInvariant().Replace("-", " - ").Insert(40, "\r\n") + "\n";
        Assert.Equal(LicenseState.Active, Eval(messy).State);
    }

    [Fact]
    public void TamperedKey_IsRejected()
    {
        var key = Issue(Device);
        int pos = 30;
        var c = key[pos] == 'A' ? 'B' : 'A';
        var bad = key[..pos] + c + key[(pos + 1)..];
        Assert.False(Eval(bad).IsPro);
    }

    [Fact]
    public void ForgedKey_SignedWithOtherKey_IsRejected()
    {
        var other = LicenseCrypto.CreateKeyPair();
        var forged = LicenseKey.Create(NewData(Device), other.privateKeyBase64);
        Assert.Equal(LicenseState.InvalidSignature, Eval(forged).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("BNX-SC-PRO-1234-5678")]
    [InlineData("BNX-SC-PRO-UUUU-UUUU")]
    public void Garbage_IsRejectedWithMessage(string input)
    {
        var st = Eval(input);
        Assert.False(st.IsPro);
        Assert.False(string.IsNullOrWhiteSpace(st.Message));
    }

    [Fact]
    public void RevokedLicense_IsRejected()
    {
        var data = NewData(Device);
        var key = LicenseKey.Create(data, Keys.Prv);
        var list = RevocationList.Create(new[] { data.LicenseId }, Keys.Prv);
        var body = RevocationList.ParseVerified(list.ToText(), new[] { Keys.Pub });
        Assert.NotNull(body);
        Assert.Equal(LicenseState.Revoked, Eval(key, Device, body!.RevokedLicenseIds).State);
    }

    [Fact]
    public void ForgedRevocationList_IsIgnored()
    {
        var other = LicenseCrypto.CreateKeyPair();
        var list = RevocationList.Create(new[] { "LIC-AAAA" }, other.privateKeyBase64);
        Assert.Null(RevocationList.ParseVerified(list.ToText(), new[] { Keys.Pub }));
        Assert.Null(RevocationList.ParseVerified("{nonsense", new[] { Keys.Pub }));
    }

    [Fact]
    public void ManyKeys_AreUnique()
    {
        var ids = new HashSet<string>();
        var keys = new HashSet<string>();
        for (int i = 0; i < 500; i++)
        {
            var d = NewData();
            Assert.True(ids.Add(d.LicenseId));
            Assert.True(keys.Add(LicenseKey.Create(d, Keys.Prv)));
        }
    }

    [Fact]
    public void PayloadRoundTrip_PreservesFields()
    {
        var d = NewData(Device);
        var back = LicenseKey.DecodePayload(LicenseKey.EncodePayload(d));
        Assert.Equal(d.LicenseId, back.LicenseId);
        Assert.Equal(Device, back.BoundDeviceId);
        Assert.Equal(LicenseType.Lifetime, back.Type);
        Assert.Equal(d.IssuedUtc.Date, back.IssuedUtc.Date);
    }

    [Fact]
    public void Base32_RoundTrips()
    {
        var rnd = new Random(1);
        for (int n = 1; n < 120; n++)
        {
            var b = new byte[n];
            rnd.NextBytes(b);
            Assert.Equal(b, Base32.Decode(Base32.Encode(b)));
        }
    }

    [Fact]
    public void Entitlements_FreeCoreFeaturesNeverLocked()
    {
        foreach (var f in new[] { Feature.FullScreenCapture, Feature.RegionCapture, Feature.WindowCapture, Feature.DelayedCapture,
                     Feature.ScreenRecording, Feature.SystemAudio, Feature.Microphone, Feature.Crop, Feature.Arrow, Feature.UndoRedo, Feature.Mp4Export })
            Assert.True(FeatureCatalog.IsAllowed(f, Edition.Free), f.ToString());
    }

    [Fact]
    public void Entitlements_ProFeaturesLockedForFree_OpenForLifetime()
    {
        foreach (var info in FeatureCatalog.All.Where(i => i.Tier == FeatureTier.Pro))
        {
            Assert.False(FeatureCatalog.IsAllowed(info.Feature, Edition.Free), info.Feature.ToString());
            Assert.True(FeatureCatalog.IsAllowed(info.Feature, Edition.LifetimePro));
        }
    }

    [Fact]
    public void EveryFeature_HasCatalogEntry()
    {
        foreach (Feature f in Enum.GetValues<Feature>()) Assert.NotNull(FeatureCatalog.Get(f));
    }

    [Fact]
    public async Task UnconfiguredServer_ReportsNotAvailable()
    {
        var c = new HttpLicenseServerClient(null);
        Assert.False(c.IsConfigured);
        Assert.False((await c.ActivateAsync("BNX-SC-PRO-X", Device, "pc")).Success);
        Assert.Null(await c.GetRevocationListAsync());
        Assert.False(new HttpLicenseServerClient("http://insecure.example").IsConfigured);
    }
}
