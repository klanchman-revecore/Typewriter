using System.Collections;
using System.Reflection;
using AwesomeAssertions;
using Typewriter.CodeModel;
using Xunit;
using Type = Typewriter.CodeModel.Type;

namespace Typewriter.Engine.Tests;

public sealed class ClassInitializationTests
{
    public static TheoryData<string, System.Type> DefaultProperties => new()
    {
        { nameof(Class.Attributes), typeof(AttributeCollection) },
        { nameof(Class.Constants), typeof(ConstantCollection) },
        { nameof(Class.Delegates), typeof(DelegateCollection) },
        { nameof(Class.Events), typeof(EventCollection) },
        { nameof(Class.Fields), typeof(FieldCollection) },
        { nameof(Class.Interfaces), typeof(InterfaceCollection) },
        { nameof(Class.Methods), typeof(MethodCollection) },
        { nameof(Class.NestedClasses), typeof(ClassCollection) },
        { nameof(Class.NestedEnums), typeof(EnumCollection) },
        { nameof(Class.NestedInterfaces), typeof(InterfaceCollection) },
        { nameof(Class.NestedRecords), typeof(RecordCollection) },
        { nameof(Class.NestedStructs), typeof(StructCollection) },
        { nameof(Class.Properties), typeof(PropertyCollection) },
        { nameof(Class.StaticReadOnlyFields), typeof(StaticReadOnlyFieldCollection) },
        { nameof(Class.Type), typeof(Type) },
        { nameof(Class.TypeArguments), typeof(TypeCollection) },
        { nameof(Class.TypeParameters), typeof(TypeParameterCollection) },
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructionLeavesDefaultsUninitialized(bool derived)
    {
        var model = derived ? new DerivedClass() : new Class();
        var defaultFields = GetDefaultFields().ToArray();

        defaultFields.Should().HaveCount(17);
        foreach (var field in defaultFields)
        {
            field.GetValue(obj: model).Should().BeNull();
        }
    }

    [Theory]
    [MemberData(memberName: nameof(DefaultProperties))]
    public void ReadingADefaultOnlyInitializesThatValue(string propertyName, System.Type valueType)
    {
        var model = new Class();
        var property = typeof(Class).GetProperty(name: propertyName)!;

        var value = property.GetValue(obj: model);

        value.Should().BeOfType(expectedType: valueType);
        var initializedValues = GetDefaultFields()
            .Select(selector: field => field.GetValue(obj: model))
            .Where(predicate: current => current is not null);
        initializedValues.Should().ContainSingle().Which.Should().BeSameAs(value);
    }

    [Theory]
    [MemberData(memberName: nameof(DefaultProperties))]
    public void DefaultsAreStableAndIndependent(string propertyName, System.Type valueType)
    {
        var property = typeof(Class).GetProperty(name: propertyName)!;
        var first = new Class();
        var second = new Class();

        var value = property.GetValue(obj: first);

        value.Should().BeOfType(expectedType: valueType);
        if (value is IEnumerable collection)
        {
            collection.Cast<object>().Should().BeEmpty();
        }

        property.GetValue(obj: first).Should().BeSameAs(value);
        property.GetValue(obj: second).Should().NotBeSameAs(value);
    }

    [Theory]
    [MemberData(memberName: nameof(DefaultProperties))]
    public void InitializersPreserveSuppliedValues(string propertyName, System.Type valueType)
    {
        var property = typeof(Class).GetProperty(name: propertyName)!;
        var value = Activator.CreateInstance(type: valueType);
        var model = new Class();

        property.SetValue(obj: model, value: value);

        property.GetValue(obj: model).Should().BeSameAs(value);
        property.GetValue(obj: model).Should().BeSameAs(value);
    }

    [Theory]
    [MemberData(memberName: nameof(DefaultProperties))]
    public void InitializersPreserveExplicitNulls(string propertyName, System.Type valueType)
    {
        var property = typeof(Class).GetProperty(name: propertyName)!;
        var model = new Class();
        var otherValue = Activator.CreateInstance(type: valueType);

        property.SetValue(obj: model, value: null);

        property.GetValue(obj: model).Should().BeNull();
        property.GetValue(obj: model).Should().BeNull();
        property.SetValue(obj: model, value: otherValue);
        property.GetValue(obj: model).Should().BeSameAs(otherValue);
    }

    [Theory]
    [MemberData(memberName: nameof(DefaultProperties))]
    public void ConcurrentFirstReadsReturnTheSameValue(string propertyName, System.Type valueType)
    {
        var property = typeof(Class).GetProperty(name: propertyName)!;
        var model = new Class();
        var values = new object?[128];

        Parallel.For(fromInclusive: 0, toExclusive: values.Length, body: index => values[index] = property.GetValue(obj: model));

        values[0].Should().BeOfType(expectedType: valueType);
        foreach (var value in values)
        {
            value.Should().BeSameAs(values[0]);
        }
    }

    [Fact]
    public void SeparateCollectionPropertiesHaveIndependentDefaults()
    {
        var model = new Class();

        model.Interfaces.Should().NotBeSameAs(model.NestedInterfaces);
        model.TypeArguments.Should().NotBeSameAs(model.Type.TypeArguments);
        model.Attributes.Should().NotBeSameAs(model.Type.Attributes);
    }

    [Fact]
    public void SuppliedMutableCollectionRetainsIdentityAndMutations()
    {
        var arguments = new MutableTypeCollection();
        var model = new Class { TypeArguments = arguments };
        var other = new Class();
        var argument = new Type { Name = "Widget" };

        arguments.Add(item: argument);

        model.TypeArguments.Should().BeSameAs(arguments);
        model.TypeArguments.Should().ContainSingle().Which.Should().BeSameAs(argument);
        other.TypeArguments.Should().BeEmpty();
        model.Type.TypeArguments.Should().BeEmpty();
    }

    [Fact]
    public void InitializingOneDefaultDoesNotChangeOthers()
    {
        var arguments = new TypeCollection(items: [new Type { Name = "Widget" }]);
        var model = new Class { TypeArguments = arguments, Attributes = null!, Type = null! };

        model.TypeArguments.Should().BeSameAs(arguments);
        model.Attributes.Should().BeNull();
        model.Type.Should().BeNull();
        model.TypeParameters.Should().BeEmpty();
        model.Events.Should().BeEmpty();
        model.Interfaces.Should().BeEmpty();
    }

    [Fact]
    public void DerivedInitAndGettersRemainVirtual()
    {
        var arguments = new TypeCollection(items: [new Type { Name = "Widget" }]);
        var type = new Type { Name = "Factory" };
        Class model = new DerivedClass { TypeArguments = arguments, Type = type };

        model.TypeArguments.Should().BeSameAs(arguments);
        model.Type.Should().BeSameAs(type);
        model.Attributes.Should().BeSameAs(model.Attributes);
    }

    [Fact]
    public void ImplicitConversionReturnsTheSameType()
    {
        var model = new Class { Name = "Factory" };

        Type? first = model;
        Type? second = model;
        string name = model;

        first.Should().BeSameAs(model.Type);
        second.Should().BeSameAs(first);
        name.Should().Be("Factory");
    }

    [Fact]
    public void DefaultTypeHasOriginalDefaults()
    {
        var model = new Class();

        model.Type.Name.Should().BeEmpty();
        model.Type.DefaultValue.Should().Be("null");
        model.Type.Settings.Should().BeNull();
        model.Type.Attributes.Should().BeEmpty();
    }

    private static IEnumerable<FieldInfo> GetDefaultFields()
    {
        return typeof(Class).GetFields(bindingAttr: BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(predicate: field => typeof(IFilterable).IsAssignableFrom(c: field.FieldType) || field.FieldType == typeof(Type));
    }

    private sealed class DerivedClass : Class
    {
        public override Type Type { get; init; } = new();

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
