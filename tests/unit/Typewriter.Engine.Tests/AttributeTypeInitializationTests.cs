using System.Reflection;
using AwesomeAssertions;
using Typewriter.Abstractions;
using Typewriter.CodeModel;
using Typewriter.Configuration;
using Xunit;
using Attribute = Typewriter.CodeModel.Attribute;
using Type = Typewriter.CodeModel.Type;

namespace Typewriter.Engine.Tests;

public sealed class AttributeTypeInitializationTests
{
    [Theory]
    [InlineData("System.DateTime")]
    [InlineData("System.DateOnly")]
    [InlineData("System.TimeOnly")]
    [InlineData("System.Guid")]
    [InlineData("System.Decimal")]
    [InlineData("System.String")]
    public void AttributeTypesKeepAdaptationTimeMappingsAndLiveSettings(string fullName)
    {
        var settings = new Settings();
        settings.UseDateType(dateType: "BeforeDate").UseDateInitializer(dateInitializer: "beforeDate()");
        settings.UseDateOnlyType(dateOnlyType: "BeforeDateOnly").UseDateOnlyInitializer(dateOnlyInitializer: "beforeDateOnly()");
        settings.UseTimeOnlyType(timeOnlyType: "BeforeTimeOnly").UseTimeOnlyInitializer(timeOnlyInitializer: "beforeTimeOnly()");
        settings.UseGuidType(guidType: "BeforeGuid").UseGuidInitializer(guidInitializer: "beforeGuid()");
        settings.UseDecimalType(decimalType: "BeforeDecimal").UseDecimalInitializer(decimalInitializer: "beforeDecimal()");
        var reference = CreateReference(fullName: fullName);
        var factory = CreateFactory(settings: settings);
        factory.TryAdapt(context: reference, targetType: typeof(Type), adapted: out var expected).Should().BeTrue();
        var expectedType = expected.Should().BeAssignableTo<Type>().Which;
        var attribute = AdaptAttribute(factory: factory, reference: reference);

        settings.UseDateType(dateType: "AfterDate").UseDateInitializer(dateInitializer: "afterDate()");
        settings.UseDateOnlyType(dateOnlyType: "AfterDateOnly").UseDateOnlyInitializer(dateOnlyInitializer: "afterDateOnly()");
        settings.UseTimeOnlyType(timeOnlyType: "AfterTimeOnly").UseTimeOnlyInitializer(timeOnlyInitializer: "afterTimeOnly()");
        settings.UseGuidType(guidType: "AfterGuid").UseGuidInitializer(guidInitializer: "afterGuid()");
        settings.UseDecimalType(decimalType: "AfterDecimal").UseDecimalInitializer(decimalInitializer: "afterDecimal()");
        settings.UseStringLiteralCharacter(ch: '\'');

        var argument = attribute.Arguments[0];
        foreach (var type in new[] { attribute.Type, argument.Type, argument.TypeValue })
        {
            type.Should().NotBeNull();
            type.Name.Should().Be(expectedType.Name);
            type.DefaultValue.Should().Be(expectedType.DefaultValue);
            type.Settings.Should().BeSameAs(settings);
        }

        factory.TryAdapt(context: reference, targetType: typeof(Type), adapted: out var laterExpected).Should().BeTrue();
        var laterType = laterExpected.Should().BeAssignableTo<Type>().Which;
        var laterAttribute = AdaptAttribute(factory: factory, reference: reference);
        laterAttribute.Type.Should().NotBeNull();
        laterAttribute.Type.Name.Should().Be(laterType.Name);
        laterAttribute.Type.DefaultValue.Should().Be(laterType.DefaultValue);
    }

    [Fact]
    public void AttributeTypesHaveStableAndSeparateIdentities()
    {
        var factory = CreateFactory(settings: new Settings());
        var reference = CreateReference(fullName: "System.String");
        var first = AdaptAttribute(factory: factory, reference: reference);
        var second = AdaptAttribute(factory: factory, reference: reference);

        first.Type.Should().NotBeNull().And.BeSameAs(first.Type).And.NotBeSameAs(second.Type);
        first.Arguments[0].Type.Should().NotBeNull().And.BeSameAs(first.Arguments[0].Type).And.NotBeSameAs(first.Type);
        first.Arguments[0].TypeValue.Should().NotBeNull().And.BeSameAs(first.Arguments[0].TypeValue).And.NotBeSameAs(first.Arguments[0].Type);
        first.Arguments[0].Value.Should().Be("typeof(System.String)");
    }

    [Fact]
    public void CustomSettingsAreReadDuringAdaptation()
    {
        var settings = new CountingSettings();
        var factory = CreateFactory(settings: settings);

        var attribute = AdaptAttribute(factory: factory, reference: CreateReference(fullName: "System.String"));

        settings.Reads.Should().BePositive();
        var reads = settings.Reads;
        attribute.Type.Should().NotBeNull();
        attribute.Arguments[0].Type.Should().NotBeNull();
        attribute.Arguments[0].TypeValue.Should().NotBeNull();
        settings.Reads.Should().Be(reads);
    }

    [Fact]
    public void PublicInitializersAndNullDefaultsRemainUnchanged()
    {
        var type = new Type { Name = "Widget" };
        var attribute = new Attribute { Type = type };
        var argument = new AttributeArgument { Type = type, TypeValue = type };

        attribute.Type.Should().BeSameAs(type);
        argument.Type.Should().BeSameAs(type);
        argument.TypeValue.Should().BeSameAs(type);
        new Attribute().Type.Should().BeNull();
        new AttributeArgument().Type.Should().BeNull();
        new AttributeArgument().TypeValue.Should().BeNull();
    }

    [Fact]
    public void NameFilteringDoesNotConstructAttributeTypes()
    {
        var attribute = AdaptAttribute(factory: CreateFactory(settings: new Settings()), reference: CreateReference(fullName: "System.String"));
        var model = new Class { Attributes = new AttributeCollection(items: [attribute]) };
        var classes = new ClassCollection(items: [model]);
        var states = GetDeferredTypes(item: attribute).Concat(second: GetDeferredTypes(item: attribute.Arguments[0])).ToArray();

        classes.AttributeFilterSelector(arg: model).Should().Contain("Example").And.Contain("Sample.ExampleAttribute");
        attribute.Value.Should().NotBeNull();
        attribute.Arguments[0].Value.Should().Be("typeof(System.String)");

        states.Should().HaveCount(3).And.OnlyContain(predicate: state => !state.IsValueCreated);
        attribute.Type.Should().NotBeNull();
        states.Count(predicate: state => state.IsValueCreated).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConcurrentReadsEvaluateEachPayloadOnce(int payload)
    {
        var calls = 0;
        var expected = new Type { Name = "Widget" };
        var model = CreateDeferredPayload(payload: payload, factory: () =>
        {
            Interlocked.Increment(location: ref calls);
            return expected;
        });
        var property = model.GetType().GetProperty(name: payload == 2 ? nameof(AttributeArgument.TypeValue) : nameof(Attribute.Type))!;
        var values = new object?[128];

        calls.Should().Be(0);
        Parallel.For(fromInclusive: 0, toExclusive: values.Length, body: index => values[index] = property.GetValue(obj: model));

        calls.Should().Be(1);
        foreach (var value in values)
        {
            value.Should().BeSameAs(expected);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NullResultsAreCached(int payload)
    {
        var calls = 0;
        var model = CreateDeferredPayload(payload: payload, factory: () =>
        {
            calls++;
            return null;
        });
        var property = model.GetType().GetProperty(name: payload == 2 ? nameof(AttributeArgument.TypeValue) : nameof(Attribute.Type))!;

        property.GetValue(obj: model).Should().BeNull();
        property.GetValue(obj: model).Should().BeNull();
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitInitCancelsDeferredEvaluation(int payload)
    {
        var model = CreateDeferredPayload(payload: payload, factory: static () => throw new InvalidOperationException());
        var property = model.GetType().GetProperty(name: payload == 2 ? nameof(AttributeArgument.TypeValue) : nameof(Attribute.Type))!;
        var expected = new Type { Name = "Widget" };

        property.SetValue(obj: model, value: expected);

        property.GetValue(obj: model).Should().BeSameAs(expected);
        property.SetValue(obj: model, value: null);
        property.GetValue(obj: model).Should().BeNull();
    }

    [Fact]
    public void MissingMetadataTypesDoNotAllocateDeferredPayloads()
    {
        var metadata = new AttributeMetadata(
            Name: "Example", FullName: "Sample.ExampleAttribute", Arguments: [new AttributeArgumentMetadata(Name: "Value", Value: "sample")]);
        var factory = CreateFactory(settings: new Settings());
        factory.TryAdapt(context: metadata, targetType: typeof(Attribute), adapted: out var adapted).Should().BeTrue();
        var attribute = adapted.Should().BeOfType<Attribute>().Which;

        GetDeferredTypes(item: attribute).Should().BeEmpty();
        GetDeferredTypes(item: attribute.Arguments[0]).Should().BeEmpty();
        attribute.Type.Should().BeNull();
        attribute.Arguments[0].Type.Should().BeNull();
        attribute.Arguments[0].TypeValue.Should().BeNull();
    }

    [Fact]
    public void NestedPayloadsUseCapturedMappingsButExistingLazyInterfacesStayLive()
    {
        var settings = new Settings().UseDateType(dateType: "BeforeDate");
        var date = CreateReference(fullName: "System.DateTime");
        var reference = CreateReference(fullName: "Sample.Container") with
        {
            TypeArguments = [date],
            ElementType = date,
            Interfaces = [CreateReference(fullName: "Sample.IContainer") with { TypeArguments = [date] }],
        };
        var attribute = AdaptAttribute(factory: CreateFactory(settings: settings), reference: reference);

        settings.UseDateType(dateType: "AfterDate");

        var type = attribute.Type;
        type.Should().NotBeNull();
        type.TypeArguments[0].Name.Should().Be("BeforeDate");
        type.ElementType.Should().NotBeNull();
        type.ElementType.Name.Should().Be("BeforeDate");
        type.Interfaces[0].TypeArguments[0].Name.Should().Be("AfterDate");
        type.TypeArguments[0].Settings.Should().BeSameAs(settings);
    }

    [Fact]
    public void DeferredErrorsAreNotHidden()
    {
        var calls = 0;
        var attribute = new Attribute();
        attribute.SetTypeFactory(factory: () =>
        {
            calls++;
            throw new InvalidOperationException(message: "Invalid type payload");
        });
        Action read = () => _ = attribute.Type;

        read.Should().Throw<InvalidOperationException>().WithMessage("Invalid type payload");
        read.Should().Throw<InvalidOperationException>().WithMessage("Invalid type payload");
        calls.Should().Be(1);
    }

    private static Item CreateDeferredPayload(int payload, Func<Type?> factory)
    {
        if (payload == 0)
        {
            var attribute = new Attribute();
            attribute.SetTypeFactory(factory: factory);
            return attribute;
        }

        var argument = new AttributeArgument();
        if (payload == 1)
        {
            argument.SetTypeFactory(factory: factory);
        }
        else
        {
            argument.SetTypeValueFactory(factory: factory);
        }

        return argument;
    }

    private static IEnumerable<Lazy<Type?>> GetDeferredTypes(Item item)
    {
        return item.GetType().GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(selector: field => field.GetValue(obj: item))
            .OfType<Lazy<Type?>>();
    }

    private static TemplateCodeModelAdapterFactory CreateFactory(Settings settings)
    {
        return new TemplateCodeModelAdapterFactory(
            metadata: new ProjectMetadata(ProjectPath: "Sample.csproj", SourceFiles: [], Types: [], Diagnostics: []),
            settings: settings);
    }

    private static Attribute AdaptAttribute(TemplateCodeModelAdapterFactory factory, TypeMetadataReference reference)
    {
        var metadata = new AttributeMetadata(
            Name: "Example",
            FullName: "Sample.ExampleAttribute",
            Arguments:
            [
                new AttributeArgumentMetadata(Name: "Value", Value: "typeof(System.String)")
                {
                    Type = reference,
                    TypeValue = reference,
                },
            ])
        {
            Type = reference,
        };
        factory.TryAdapt(context: metadata, targetType: typeof(Attribute), adapted: out var adapted).Should().BeTrue();
        return adapted.Should().BeOfType<Attribute>().Which;
    }

    private static TypeMetadataReference CreateReference(string fullName)
    {
        var lastDot = fullName.LastIndexOf(value: '.');
        return new TypeMetadataReference(
            Name: fullName[(lastDot + 1)..],
            FullName: fullName,
            Namespace: fullName[..lastDot],
            IsNullable: false,
            IsCollection: false,
            IsDictionary: false,
            IsEnum: false,
            IsPrimitive: fullName is "System.String" or "System.Decimal",
            IsDateLike: fullName is "System.DateTime" or "System.DateOnly" or "System.TimeOnly",
            ElementType: null,
            TypeArguments: []);
    }

    private sealed class CountingSettings : Settings
    {
        public int Reads { get; private set; }

        public override bool StrictNullGeneration
        {
            get
            {
                Reads++;
                return base.StrictNullGeneration;
            }
        }
    }
}
