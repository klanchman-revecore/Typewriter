using System.Reflection;
using AwesomeAssertions;
using Typewriter.Abstractions;
using Typewriter.CodeModel;
using Typewriter.Configuration;
using Xunit;
using Attribute = Typewriter.CodeModel.Attribute;
using File = Typewriter.CodeModel.File;
using Type = Typewriter.CodeModel.Type;

namespace Typewriter.Engine.Tests;

public sealed class DeferredCodeModelTests
{
    private static readonly string[] Names = ["First", "Second"];

    [Fact]
    public void InternalCollectionsOnlyAdaptAccessedItemsAndSnapshotMembership()
    {
        var calls = 0;
        var source = new List<string> { "First", "Second" };
        var items = DeferredItemList<Class>.Create(source: source, selector: name =>
        {
            Interlocked.Increment(location: ref calls);
            return new Class { Name = name };
        });
        var collection = new ClassCollection(items: items);
        source.Add(item: "Third");

        collection.Count.Should().Be(2);
        calls.Should().Be(0);
        var first = collection[0];
        collection[0].Should().BeSameAs(first);
        calls.Should().Be(1);
        collection.Select(selector: item => item.Name).Should().Equal("First", "Second");
        calls.Should().Be(2);
    }

    [Fact]
    public void PublicCollectionConstructionRemainsEager()
    {
        var calls = 0;
        var source = Names.Select(selector: name =>
        {
            calls++;
            return new Class { Name = name };
        });

        var collection = new ClassCollection(items: source);

        calls.Should().Be(2);
        collection.Count.Should().Be(2);
        collection[0].Should().BeSameAs(collection[0]);
        calls.Should().Be(2);
    }

    [Fact]
    public void ConcurrentReadsOnlyAdaptEachItemOnce()
    {
        var calls = 0;
        var collection = new ClassCollection(items: DeferredItemList<Class>.Create(source: Names.Take(count: 1), selector: name =>
        {
            Interlocked.Increment(location: ref calls);
            return new Class { Name = name };
        }));
        var values = new Class[128];

        Parallel.For(fromInclusive: 0, toExclusive: values.Length, body: index => values[index] = collection[0]);

        calls.Should().Be(1);
        values.Should().OnlyContain(predicate: item => ReferenceEquals(item, values[0]));
    }

    [Fact]
    public void DeferredFailuresAreNotHiddenOrRetried()
    {
        var calls = 0;
        var collection = new ClassCollection(items: DeferredItemList<Class>.Create(source: Names.Take(count: 1), selector: _ =>
        {
            calls++;
            throw new InvalidOperationException(message: "Adaptation failed");
        }));
        Action read = () => _ = collection[0];

        read.Should().Throw<InvalidOperationException>().WithMessage("Adaptation failed");
        read.Should().Throw<InvalidOperationException>().WithMessage("Adaptation failed");
        calls.Should().Be(1);
    }

    [Theory]
    [InlineData(TypeMetadataKind.Class, nameof(File.Classes))]
    [InlineData(TypeMetadataKind.Record, nameof(File.Records))]
    [InlineData(TypeMetadataKind.Struct, nameof(File.Structs))]
    [InlineData(TypeMetadataKind.Interface, nameof(File.Interfaces))]
    [InlineData(TypeMetadataKind.Enum, nameof(File.Enums))]
    public void FileConstructionDoesNotAdaptRootNodes(TypeMetadataKind kind, string propertyName)
    {
        var metadata = CreateProject(kind: kind);
        var factory = new TemplateCodeModelAdapterFactory(metadata: metadata, settings: new Settings());

        var file = factory.CreateFile(project: metadata);
        var collection = typeof(File).GetProperty(name: propertyName)!.GetValue(obj: file)!;
        var states = GetItemStates(collection: collection);
        var typeStates = GetItemStates(collection: file.Types);

        states.Should().ContainSingle().Which.Should().BeFalse();
        typeStates.Should().ContainSingle().Which.Should().BeFalse();
        file.Types.Count.Should().Be(1);
        GetItemStates(collection: file.Types).Should().OnlyContain(predicate: created => !created);
        var first = ((System.Collections.IEnumerable)collection).Cast<Item>().First();
        ((System.Collections.IEnumerable)collection).Cast<Item>().First().Should().BeSameAs(first);
        GetItemStates(collection: collection).Should().OnlyContain(predicate: created => created);
        GetItemStates(collection: file.Types).Should().OnlyContain(predicate: created => !created);
        factory.CreateFile(project: metadata).Should().BeSameAs(file);
    }

    [Fact]
    public void DeferredRootsAndAttributesKeepCapturedMappingsAndLiveSettings()
    {
        var reference = CreateReference();
        var metadata = CreateProject(kind: TypeMetadataKind.Class, reference: reference);
        var settings = new Settings();
        settings.UseDateType(dateType: "BeforeDate");
        var factory = new TemplateCodeModelAdapterFactory(metadata: metadata, settings: settings);
        var file = factory.CreateFile(project: metadata);

        settings.UseDateType(dateType: "AfterDate");
        var model = file.Classes[0];

        model.TypeArguments[0].Name.Should().Be("BeforeDate");
        model.TypeArguments[0].Settings.Should().BeSameAs(settings);
        GetItemStates(collection: model.Attributes).Should().OnlyContain(predicate: created => !created);
        var attribute = model.Attributes[0];
        GetItemStates(collection: attribute.Arguments).Should().OnlyContain(predicate: created => !created);
        attribute.Type!.Name.Should().Be("BeforeDate");
        attribute.Arguments[0].Type!.Name.Should().Be("BeforeDate");
        attribute.Arguments[0].Type!.Settings.Should().BeSameAs(settings);
        factory.TryAdapt(context: reference, targetType: typeof(Type), adapted: out var later).Should().BeTrue();
        later.Should().BeAssignableTo<Type>().Which.Name.Should().Be("AfterDate");
    }

    [Fact]
    public void AttributeNameFilteringDoesNotFormatValuesOrAdaptArguments()
    {
        var metadata = CreateProject(kind: TypeMetadataKind.Class, reference: CreateReference());
        var model = new TemplateCodeModelAdapterFactory(metadata: metadata, settings: new Settings()).CreateFile(project: metadata).Classes[0];
        var attribute = model.Attributes[0];
        var value = typeof(Attribute).GetField(name: "_deferredValue", bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
            .GetValue(obj: attribute).Should().BeOfType<Lazy<string>>().Which;

        new ClassCollection().AttributeFilterSelector(arg: model).Should().Contain("Example");

        value.IsValueCreated.Should().BeFalse();
        GetItemStates(collection: attribute.Arguments).Should().OnlyContain(predicate: created => !created);
        attribute.Value.Should().Be("Value = \"hello\"");
        value.IsValueCreated.Should().BeTrue();
        attribute.Arguments[0].Value.Should().Be("hello");
    }

    [Fact]
    public void SharedIndexReusesMapperButNotModelsOrSettings()
    {
        var metadata = CreateProject(kind: TypeMetadataKind.Class, reference: CreateReference());
        var index = ProjectMetadataIndex.Create(metadata: metadata);
        var firstSettings = new Settings();
        var secondSettings = new Settings();
        firstSettings.UseDateType(dateType: "FirstDate");
        secondSettings.UseDateType(dateType: "SecondDate");
        var first = new TemplateCodeModelAdapterFactory(metadata: metadata, settings: firstSettings, metadataIndex: index);
        var second = new TemplateCodeModelAdapterFactory(metadata: metadata, settings: secondSettings, metadataIndex: index);
        var mapperField = typeof(TemplateCodeModelAdapterFactory).GetField(name: "_typeMapper", bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;

        mapperField.GetValue(obj: first).Should().BeSameAs(index.TypeMapper);
        mapperField.GetValue(obj: second).Should().BeSameAs(index.TypeMapper);
        var firstModel = first.CreateFile(project: metadata).Classes[0];
        var secondModel = second.CreateFile(project: metadata).Classes[0];
        firstModel.Should().NotBeSameAs(secondModel);
        firstModel.TypeArguments[0].Name.Should().Be("FirstDate");
        secondModel.TypeArguments[0].Name.Should().Be("SecondDate");
        firstModel.TypeArguments[0].Settings.Should().BeSameAs(firstSettings);
        secondModel.TypeArguments[0].Settings.Should().BeSameAs(secondSettings);
    }

    private static bool[] GetItemStates(object collection)
    {
        var baseType = collection.GetType().BaseType!;
        var items = baseType.GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(predicate: field => field.Name == "_items").GetValue(obj: collection)!;
        var deferred = items.GetType().GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(selector: field => field.GetValue(obj: items)).OfType<Array>().Single();
        return deferred.Cast<object>()
            .Select(selector: item => (bool)item.GetType().GetProperties(bindingAttr: BindingFlags.Instance | BindingFlags.Public)
                .Single(predicate: property => property.Name == "IsValueCreated").GetValue(obj: item)!)
            .ToArray();
    }

    private static ProjectMetadata CreateProject(TypeMetadataKind kind, TypeMetadataReference? reference = null)
    {
        IReadOnlyList<AttributeMetadata> attributes = [];
        if (reference is not null)
        {
            attributes =
            [
                new AttributeMetadata(Name: "Example", FullName: "Sample.ExampleAttribute", Arguments:
                [
                    new AttributeArgumentMetadata(Name: "Value", Value: "hello") { Type = reference },
                ]) { Type = reference },
            ];
        }

        var type = new TypeMetadata(
            Name: "Example",
            FullName: "Sample.Example",
            Namespace: "Sample",
            Kind: kind,
            Accessibility: MetadataAccessibility.Public,
            Properties: [],
            Attributes: attributes,
            BaseTypes: [],
            EnumValues: [],
            IsNullableAware: true)
        {
            TypeArguments = reference is null ? [] : [reference],
        };
        return new ProjectMetadata(ProjectPath: "Sample.csproj", SourceFiles: [], Types: [type], Diagnostics: []);
    }

    private static TypeMetadataReference CreateReference() => new(
        Name: "DateTime",
        FullName: "System.DateTime",
        Namespace: "System",
        IsNullable: false,
        IsCollection: false,
        IsDictionary: false,
        IsEnum: false,
        IsPrimitive: false,
        IsDateLike: true,
        ElementType: null,
        TypeArguments: []);
}
