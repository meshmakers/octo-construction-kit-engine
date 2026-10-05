using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5531: the in-memory Secret value never hands out its plaintext.
/// </summary>
public class RtSecretValueTests
{
    private const string Envelope = "enc:v2:k1:AAECAwQFBgcICQoLce30XInwk1La5ADQECWjFW2r_6nUXsA";

    private sealed class TestEntity : RtTypeWithAttributes
    {
        protected override string GetLocation() => "test";
    }

    [Fact]
    public void ToString_IsMasked_InEveryState()
    {
        Assert.Equal("***", RtSecretValue.Pending("hunter2").ToString());
        Assert.Equal("***", RtSecretValue.LegacyPlaintext("hunter2").ToString());
        Assert.Equal("***", RtSecretValue.Protected(Envelope).ToString());
        Assert.Equal("value: ***", $"value: {RtSecretValue.Pending("hunter2")}");
    }

    [Fact]
    public void States_AndEnvelope()
    {
        var protectedValue = RtSecretValue.Protected(Envelope);

        Assert.True(protectedValue.IsProtected);
        Assert.Equal(Envelope, protectedValue.Envelope);
        Assert.Equal("k1", protectedValue.KeyId);
        Assert.True(RtSecretValue.Pending("x").IsPending);
        Assert.Null(RtSecretValue.Pending("x").Envelope);
        Assert.True(RtSecretValue.LegacyPlaintext("x").IsLegacyPlaintext);
        Assert.Null(RtSecretValue.LegacyPlaintext("x").Envelope);
    }

    [Fact]
    public void Protected_RejectsNonEnvelope_WithoutEchoingIt()
    {
        var exception = Assert.Throws<ArgumentException>(() => RtSecretValue.Protected("hunter2"));
        Assert.DoesNotContain("hunter2", exception.Message);
        Assert.Throws<ArgumentException>(() => RtSecretValue.Protected("enc:v1:oKGio6SlpqeoqaqrOxh7H702oGiyoSGkNg1vJ7bb2F42vG3NFkjx4iY="));
    }

    [Fact]
    public void Equality_OnEnvelope()
    {
        Assert.Equal(RtSecretValue.Protected(Envelope), RtSecretValue.Protected(Envelope));
        Assert.True(RtSecretValue.Protected(Envelope) == RtSecretValue.Protected(Envelope));
        Assert.Equal(RtSecretValue.Protected(Envelope).GetHashCode(), RtSecretValue.Protected(Envelope).GetHashCode());
        Assert.NotEqual(RtSecretValue.Protected(Envelope), RtSecretValue.LegacyPlaintext(Envelope));
        Assert.NotEqual(RtSecretValue.Pending("a"), RtSecretValue.Pending("b"));
    }

    [Fact]
    public void NoPublicPlaintextGetter()
    {
        var publicStringProperties = typeof(RtSecretValue).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        Assert.Equal(["Envelope", "KeyId"], publicStringProperties.Order().ToList());
    }

    [Fact]
    public void Accessors_OnEntity()
    {
        var entity = new TestEntity();
        entity.SetAttributeValue("Password", AttributeValueTypesDto.Secret, "hunter2");
        entity.SetAttributeRawValue("Legacy", "old");

        var value = entity.GetAttributeSecretValueOrDefault("Password");
        Assert.NotNull(value);
        Assert.True(value.IsPending);
        Assert.True(entity.GetAttributeSecretValueOrDefault("Legacy")!.IsLegacyPlaintext);
        Assert.Null(entity.GetAttributeSecretValueOrDefault("Missing"));

        var exception = Assert.Throws<InvalidAttributeValueException>(() => entity.GetAttributeStringValue("Password"));
        Assert.Contains("Secret attribute", exception.Message);
        Assert.DoesNotContain("hunter2", exception.Message);
        Assert.Throws<InvalidAttributeValueException>(() => entity.GetAttributeStringValueOrDefault("Password"));
    }

    [Fact]
    public void Converter_MapsInputToPending_WithoutTrimming()
    {
        Assert.Equal(typeof(RtSecretValue), AttributeValueConverter.GetDotNetType(AttributeValueTypesDto.Secret));

        var converted = AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, " pw ");
        var pending = Assert.IsType<RtSecretValue>(converted);
        Assert.True(pending.IsPending);
        Assert.Equal(" pw ", pending.RawValue);

        var fromJValue = (RtSecretValue)AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret,
            new JValue("x"))!;
        Assert.Equal("x", fromJValue.RawValue);

        var protectedValue = RtSecretValue.Protected(Envelope);
        Assert.Same(protectedValue,
            AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, protectedValue));
        Assert.Null(AttributeValueConverter.ConvertAttributeValue(AttributeValueTypesDto.Secret, null));
    }
}
