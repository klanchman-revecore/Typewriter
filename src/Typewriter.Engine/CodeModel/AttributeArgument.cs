namespace Typewriter.CodeModel;

/// <summary>
/// A single argument of an <see cref="Attribute"/>, enumerated in templates with
/// <c>$Arguments[...]</c>. Named arguments expose the argument name through <c>Name</c>.
/// </summary>
public class AttributeArgument : Item
{
    private Type? _type;
    private Type? _typeValue;
    private Lazy<Type?>? _deferredType;
    private Lazy<Type?>? _deferredTypeValue;

    /// <summary>
    /// Gets the declared type of the argument value.
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
    /// Gets the referenced type when the argument is a <c>typeof(...)</c> expression,
    /// otherwise <c>null</c>.
    /// </summary>
    public virtual Type? TypeValue
    {
        get
        {
            return _deferredTypeValue is { } deferred ? deferred.Value : _typeValue;
        }

        init
        {
            _typeValue = value;
            _deferredTypeValue = null;
        }
    }

    /// <summary>
    /// Gets the constant value of the argument. Template shorthand: <c>$Value</c>.
    /// </summary>
    public virtual object? Value { get; init; }

    internal void SetTypeFactory(Func<Type?> factory) => _deferredType = new Lazy<Type?>(valueFactory: factory);

    internal void SetTypeValueFactory(Func<Type?> factory) => _deferredTypeValue = new Lazy<Type?>(valueFactory: factory);
}
