using System.Collections;
using System.Reflection;
using AwesomeAssertions;
using Typewriter.CodeModel;
using Xunit;
using Type = Typewriter.CodeModel.Type;

namespace Typewriter.Engine.Tests;

public sealed class TypeCollectionInitializationTests
{
    public static TheoryData<string, System.Type> CollectionProperties => new()
    {
        { nameof(Type.Attributes), typeof(AttributeCollection) },
        { nameof(Type.Constants), typeof(ConstantCollection) },
        { nameof(Type.Delegates), typeof(DelegateCollection) },
        { nameof(Type.Fields), typeof(FieldCollection) },
        { nameof(Type.Interfaces), typeof(InterfaceCollection) },
        { nameof(Type.Methods), typeof(MethodCollection) },
        { nameof(Type.NestedClasses), typeof(ClassCollection) },
        { nameof(Type.NestedEnums), typeof(EnumCollection) },
        { nameof(Type.NestedInterfaces), typeof(InterfaceCollection) },
        { nameof(Type.NestedRecords), typeof(RecordCollection) },
        { nameof(Type.NestedStructs), typeof(StructCollection) },
        { nameof(Type.Properties), typeof(PropertyCollection) },
        { nameof(Type.StaticReadOnlyFields), typeof(StaticReadOnlyFieldCollection) },
        { nameof(Type.TypeArguments), typeof(TypeCollection) },
        { nameof(Type.TypeParameters), typeof(TypeParameterCollection) },
        { nameof(Type.TupleElements), typeof(FieldCollection) },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructionLeavesDefaultCollectionsUninitialized(bool mapped)
    {
        Type type = mapped
            ? new MappedCodeType(interfacesFactory: static () => new InterfaceCollection())
            : new Type();
        var collectionFields = typeof(Type).GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(predicate: field => typeof(IFilterable).IsAssignableFrom(c: field.FieldType))
            .ToArray();

        collectionFields.Should().HaveCount(16);
        foreach (var field in collectionFields)
        {
            field.GetValue(obj: type).Should().BeNull();
        }
    }

    [Fact]
    public void ReadingADefaultOnlyInitializesThatCollection()
    {
        var type = new Type();

        var arguments = type.TypeArguments;

        var initializedCollections = typeof(Type).GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(predicate: field => typeof(IFilterable).IsAssignableFrom(c: field.FieldType))
            .Select(selector: field => field.GetValue(obj: type))
            .Where(predicate: value => value is not null);
        initializedCollections.Should().ContainSingle().Which.Should().BeSameAs(arguments);
    }

    [Theory]
    [MemberData(memberName: nameof(CollectionProperties))]
    public void DefaultsAreEmptyStableAndIndependent(string propertyName, System.Type collectionType)
    {
        var property = typeof(Type).GetProperty(name: propertyName)!;
        var first = new Type();
        var second = new Type();

        var collection = property.GetValue(obj: first);

        collection.Should().BeOfType(expectedType: collectionType);
        ((IEnumerable)collection).Cast<object>().Should().BeEmpty();
        property.GetValue(obj: first).Should().BeSameAs(collection);
        property.GetValue(obj: second).Should().NotBeSameAs(collection);
    }

    [Theory]
    [MemberData(memberName: nameof(CollectionProperties))]
    public void InitializersPreserveSuppliedCollections(string propertyName, System.Type collectionType)
    {
        var property = typeof(Type).GetProperty(name: propertyName)!;
        var collection = Activator.CreateInstance(type: collectionType);
        var type = new Type();

        property.SetValue(obj: type, value: collection);

        property.GetValue(obj: type).Should().BeSameAs(collection);
        property.GetValue(obj: type).Should().BeSameAs(collection);
    }

    [Theory]
    [MemberData(memberName: nameof(CollectionProperties))]
    public void InitializersPreserveExplicitNulls(string propertyName, System.Type collectionType)
    {
        var property = typeof(Type).GetProperty(name: propertyName)!;
        var type = new Type();
        var otherCollection = Activator.CreateInstance(type: collectionType);

        property.SetValue(obj: type, value: null);

        property.GetValue(obj: type).Should().BeNull();
        property.GetValue(obj: type).Should().BeNull();
        property.SetValue(obj: type, value: otherCollection);
        property.GetValue(obj: type).Should().BeSameAs(otherCollection);
    }

    [Theory]
    [MemberData(memberName: nameof(CollectionProperties))]
    public void ConcurrentFirstReadsReturnTheSameCollection(string propertyName, System.Type collectionType)
    {
        var property = typeof(Type).GetProperty(name: propertyName)!;
        var type = new Type();
        var collections = new object?[128];

        Parallel.For(fromInclusive: 0, toExclusive: collections.Length, body: index => collections[index] = property.GetValue(obj: type));

        collections[0].Should().BeOfType(expectedType: collectionType);
        foreach (var collection in collections)
        {
            collection.Should().BeSameAs(collections[0]);
        }
    }

    [Fact]
    public void SeparateCollectionPropertiesHaveIndependentDefaults()
    {
        var type = new Type();

        type.Fields.Should().NotBeSameAs(type.TupleElements);
        type.Interfaces.Should().NotBeSameAs(type.NestedInterfaces);
    }

    [Fact]
    public void SuppliedMutableCollectionRetainsIdentityAndMutations()
    {
        var arguments = new MutableTypeCollection();
        var type = new Type { TypeArguments = arguments };
        var other = new Type();
        var argument = new Type { Name = "Widget" };

        arguments.Add(item: argument);

        type.TypeArguments.Should().BeSameAs(arguments);
        type.TypeArguments.Should().ContainSingle().Which.Should().BeSameAs(argument);
        other.TypeArguments.Should().BeEmpty();
    }

    [Fact]
    public void InitializingOneCollectionDoesNotChangeOtherDefaults()
    {
        var arguments = new TypeCollection(items: [new Type { Name = "Widget" }]);
        var type = new Type { TypeArguments = arguments, Attributes = null! };

        type.TypeArguments.Should().BeSameAs(arguments);
        type.Attributes.Should().BeNull();
        type.TypeParameters.Should().BeEmpty();
        type.TupleElements.Should().BeEmpty();
        type.Interfaces.Should().BeEmpty();
    }

    [Fact]
    public void DerivedInitAndGetterRemainVirtual()
    {
        var arguments = new TypeCollection(items: [new Type { Name = "Widget" }]);
        Type type = new DerivedType { TypeArguments = arguments };

        type.TypeArguments.Should().BeSameAs(arguments);
        type.Attributes.Should().BeSameAs(type.Attributes);
    }

    [Fact]
    public void MappedInterfacesRemainLazyAndStable()
    {
        var calls = 0;
        var interfaces = new InterfaceCollection(items: [new Interface { Name = "IWidget" }]);
        Type type = new MappedCodeType(interfacesFactory: () =>
        {
            calls++;
            return interfaces;
        });

        calls.Should().Be(0);
        type.Attributes.Should().BeEmpty();
        calls.Should().Be(0);
        type.Interfaces.Should().BeSameAs(interfaces);
        type.Interfaces.Should().BeSameAs(interfaces);
        calls.Should().Be(1);
    }

    [Fact]
    public void MappedInterfaceInitializerBypassesFactory()
    {
        var interfaces = new InterfaceCollection();
        Type type = new MappedCodeType(interfacesFactory: () => throw new InvalidOperationException())
        {
            Interfaces = interfaces,
        };

        type.Interfaces.Should().BeSameAs(interfaces);
        type.Interfaces.Should().BeSameAs(interfaces);
    }

    private sealed class DerivedType : Type
    {
        public override ITypeCollection TypeArguments { get; init; } = new TypeCollection();
    }

    private sealed class MutableTypeCollection : ITypeCollection
    {
        private readonly List<Type> _items = [];

        public int Count => _items.Count;

        public Func<Item, IEnumerable<string>> AttributeFilterSelector => _ => [];

        public Func<Item, IEnumerable<string>> InheritanceFilterSelector => _ => [];

        public Func<Item, IEnumerable<string>> ItemFilterSelector => item => [item.Name];

        public Type this[int index] => _items[index: index];

        public void Add(Type item) => _items.Add(item: item);

        public IEnumerator<Type> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
