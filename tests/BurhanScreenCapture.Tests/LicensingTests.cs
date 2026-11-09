using BurhanScreenCapture.Licensing;
using Xunit;

public class LicensingTests
{
    private static readonly (string Pub, string Prv) Keys = LicenseCrypto.CreateKeyPair();
    private const string Device = "ABCD-1234-EF56-7890";

    private static SignedLicense Issue(LicensePlan plan, DateTime? expires, string device = Device, string? key = null) =>
        LicenseCrypto.Sign(new LicenseData
        {
            LicenseId = "test",
            LicenseKey = key ?? LicenseKey.Generate(),
            Plan = plan,
            DeviceId = device,
            IssuedUtc = DateTime.UtcNow,
            ExpiresUtc = expires
        }, Keys.Prv);

    [Fact]
    public void NoLicense_IsFree()
    {
        var st = LicenseEvaluator.Evaluate(null, new[] { Keys.Pub }, Device, DateTime.UtcNow, null);
        Assert.Equal(Edition.Free, st.Edition);
        Assert.False(st.IsPro);
    }

    [Theory]
    [InlineData(LicensePlan.Monthly)]
    [InlineData(LicensePlan.Yearly)]
    [InlineData(LicensePlan.Lifetime)]
    public void ValidLicense_IsPro(LicensePlan plan)
    {
        var exp = plan == LicensePlan.Lifetime ? (DateTime?)null : DateTime.UtcNow.AddDays(10);
        var st = LicenseEvaluator.Evaluate(Issue(plan, exp), new[] { Keys.Pub }, Device, DateTime.UtcNow, null);
        Assert.Equal(LicenseState.Active, st.State);
        Assert.Equal(Edition.Pro, st.Edition);
    }

    [Fact]
    public void RoundTripThroughFileText_StillValid()
    {
        var text = Issue(LicensePlan.Yearly, DateTime.UtcNow.AddDays(100)).ToFileText();
        var st = LicenseEvaluator.Evaluate(SignedLicense.Parse(text), new[] { Keys.Pub }, Device, DateTime.UtcNow, null);
        Assert.Equal(LicenseState.Active, st.State);
    }

    [Fact]
    public void TamperedPayload_IsRejected()
    {
        var lic = Issue(LicensePlan.Monthly, DateTime.UtcNow.AddDays(1));
        var json = SignedLicense.Utf8(lic.PayloadBytes).Replace("\"Monthly\"", "\"Lifetime\"");
        lic.PayloadBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        var st = LicenseEvaluator.Evaluate(lic, new[] { Keys.Pub }, Device, DateTime.UtcNow, null);
        Assert.Equal(LicenseState.InvalidSignature, st.State);
        Assert.False(st.IsPro);
    }

    [Fact]
    public void LicenseFromOtherKey_IsRejected()
    {
        var other = LicenseCrypto.CreateKeyPair();
        var st = LicenseEvaluator.Evaluate(Issue(LicensePlan.Lifetime, null), new[] { other.publicKeyBase64 }, Device, DateTime.UtcNow, null);
        Assert.Equal(LicenseState.InvalidSignature, st.State);
    }

    [Fact]
    public void OtherDevice_IsRejected()
    {
        var st = LicenseEvaluator.Evaluate(Issue(LicensePlan.Lifetime, null), new[] { Keys.Pub }, "FFFF-0000-FFFF-0000", DateTime.UtcNow, null);
        Assert.Equal(LicenseState.WrongDevice, st.State);
        Assert.False(st.IsPro);
    }

    [Fact]
    public void Expired_FallsBackToFree()
    {
        var st = LicenseEvaluator.Evaluate(Issue(LicensePlan.Monthly, DateTime.UtcNow.AddMinutes(-1)), new[] { Keys.Pub }, Device, DateTime.UtcNow, null);
        Assert.Equal(LicenseState.Expired, st.State);
        Assert.Equal(Edition.Free, st.Edition);
    }

    [Fact]
    public void Trial_IsProTrial_ThenExpires()
    {
        var lic = Issue(LicensePlan.Trial, DateTime.UtcNow.AddDays(7));
        Assert.Equal(Edition.ProTrial, LicenseEvaluator.Evaluate(lic, new[] { Keys.Pub }, Device, DateTime.UtcNow, null).Edition);
        Assert.Equal(LicenseState.Expired, LicenseEvaluator.Evaluate(lic, new[] { Keys.Pub }, Device, DateTime.UtcNow.AddDays(8), null).State);
    }

    [Fact]
    public void ClockMovedBack_IsDetected()
    {
        var now = DateTime.UtcNow;
        var st = LicenseEvaluator.Evaluate(Issue(LicensePlan.Monthly, now.AddDays(20)), new[] { Keys.Pub }, Device, now, now.AddDays(10));
        Assert.Equal(LicenseState.ClockTampered, st.State);
    }

    [Fact]
    public void CorruptedFile_Throws()
    {
        Assert.Throws<LicenseFormatException>(() => SignedLicense.Parse("not json"));
        Assert.Throws<LicenseFormatException>(() => SignedLicense.Parse("{\"payload\":\"###\",\"signature\":\"AA==\"}"));
    }

    [Fact]
    public void LicenseKeys_AreWellFormed_AndTyposDetected()
    {
        for (int i = 0; i < 200; i++)
        {
            var k = LicenseKey.Generate();
            Assert.True(LicenseKey.IsWellFormed(k), k);
            Assert.True(LicenseKey.IsWellFormed(k.ToLowerInvariant().Replace("-", " ")));
        }
        var key = LicenseKey.Generate();
        var chars = key.ToCharArray();
        chars[6] = chars[6] == 'A' ? 'B' : 'A';
        Assert.False(LicenseKey.IsWellFormed(new string(chars)));
        Assert.False(LicenseKey.IsWellFormed("BSCP-AAAAA"));
        Assert.False(LicenseKey.IsWellFormed(""));
    }

    [Fact]
    public void Entitlements_FreeCoreFeaturesNeverLocked()
    {
        var mustBeFree = new[]
        {
            Feature.FullScreenCapture, Feature.RegionCapture, Feature.WindowCapture, Feature.ScreenRecording,
            Feature.SystemAudio, Feature.Microphone, Feature.Crop, Feature.Arrow, Feature.UndoRedo, Feature.Mp4Export
        };
        foreach (var f in mustBeFree) Assert.True(FeatureCatalog.IsAllowed(f, Edition.Free), f.ToString());
    }

    [Fact]
    public void Entitlements_ProFeaturesLockedForFree_OpenForProAndTrial()
    {
        foreach (var info in FeatureCatalog.All.Where(i => i.Tier == FeatureTier.Pro))
        {
            Assert.False(FeatureCatalog.IsAllowed(info.Feature, Edition.Free), info.Feature.ToString());
            Assert.True(FeatureCatalog.IsAllowed(info.Feature, Edition.Pro));
            Assert.True(FeatureCatalog.IsAllowed(info.Feature, Edition.ProTrial));
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
        var r = await c.ActivateAsync(LicenseKey.Generate(), Device, "pc");
        Assert.False(r.Success);
        Assert.False(new HttpLicenseServerClient("http://insecure.example").IsConfigured);
    }
}
