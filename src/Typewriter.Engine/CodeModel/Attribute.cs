namespace Typewriter.CodeModel;

/// <summary>
/// An attribute applied to a code model item, enumerated in templates with
/// <c>$Attributes[...]</c>. Attribute filters use the short name, for example
/// <c>$Classes([Serializable])[...]</c>.
/// </summary>
public class Attribute : Item
{
    private Type? _type;
    private Lazy<Type?>? _deferredType;
    private string _value = string.Empty;
    private Lazy<string>? _deferredValue;

    /// <summary>
    /// Gets the constructor and named arguments of the attribute.
    /// Template shorthand: <c>$Arguments[...]</c>.
    /// </summary>
    public virtual IAttributeArgumentCollection Arguments { get; init; } = new AttributeArgumentCollection();

    /// <summary>
    /// Gets the type of the attribute class.
    /// </summary>
    public virtual Type? Type
    {
        get
        {
            return _deferredType is { } deferred ? deferred.Value : _type;
        }

        init
        {
            _type = value;
            _deferredType = null;
        }
    }

    /// <summary>
    /// Gets the raw argument list of the attribute as written in source, for example
    /// <c>"users", Order = 1</c> for <c>[Route("users", Order = 1)]</c>.
    /// Template shorthand: <c>$Value</c>.
    /// </summary>
    public virtual string Value
    {
        get
        {
            return _deferredValue is { } deferred ? deferred.Value : _value;
        }

        init
        {
            _value = value;
            _deferredValue = null;
        }
    }

    /// <summary>
    /// Converts the attribute to its name so it can be used directly in string contexts.
    /// </summary>
    public static implicit operator string(Attribute? instance) => instance?.ToString() ?? string.Empty;

    internal void SetTypeFactory(Func<Type?> factory) => _deferredType = new Lazy<Type?>(valueFactory: factory);

    internal void SetValueFactory(Func<string> factory) => _deferredValue = new Lazy<string>(valueFactory: factory);
}
