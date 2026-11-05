using System.Globalization;
using System.Text;
using Typewriter.Abstractions;

namespace Typewriter.Engine;

/// <summary>
/// Debug-only summaries of the input shape, emitted through the <c>TYPEWRITER_TRACE_PERF</c> trace.
/// </summary>
internal static class GenerationStatistics
{
    public static string DescribeProject(ProjectMetadata project, ProjectMetadataIndex index)
    {
        var types = index.TypesByFullName.Values.ToArray();
        var topLevel = project.Types.Count(predicate: static type => string.IsNullOrWhiteSpace(value: type.ContainingTypeFullName));
        var byKind = types
            .GroupBy(keySelector: static type => type.Kind)
            .OrderBy(keySelector: static group => group.Key)
            .Select(selector: static group => string.Create(provider: CultureInfo.InvariantCulture, handler: $"{group.Key}={group.Count()}"));
        var byAccessibility = types
            .GroupBy(keySelector: static type => type.Accessibility)
            .OrderBy(keySelector: static group => group.Key)
            .Select(selector: static group => string.Create(provider: CultureInfo.InvariantCulture, handler: $"{group.Key}={group.Count()}"));
        var assemblies = types
            .GroupBy(keySelector: static type => type.AssemblyName, comparer: StringComparer.Ordinal)
            .OrderByDescending(keySelector: static group => group.Count())
            .Select(selector: static group => string.Create(provider: CultureInfo.InvariantCulture, handler: $"{(string.IsNullOrEmpty(value: group.Key) ? "<none>" : group.Key)}={group.Count()}"));

        var properties = types.Sum(selector: static type => type.Properties.Count);
        var methods = types.Sum(selector: static type => type.Methods.Count);
        var parameters = types.Sum(selector: static type => type.Methods.Sum(selector: static method => method.Parameters.Count));
        var fields = types.Sum(selector: static type => type.Fields.Count + type.StaticReadOnlyFields.Count);
        var constants = types.Sum(selector: static type => type.Constants.Count);
        var enumValues = types.Sum(selector: static type => type.EnumValues.Count);
        var attributes = types.Sum(selector: static type =>
            type.Attributes.Count
            + type.Properties.Sum(selector: static property => property.Attributes.Count)
            + type.Methods.Sum(selector: static method => method.Attributes.Count + method.Parameters.Sum(selector: static parameter => parameter.Attributes.Count)));
        var maxProperties = types.Length == 0 ? 0 : types.Max(selector: static type => type.Properties.Count);
        var maxMethods = types.Length == 0 ? 0 : types.Max(selector: static type => type.Methods.Count);
        var typesPerFile = project.SourceFiles.Select(selector: static file => file.Types.Count).Order().ToArray();

        var builder = new StringBuilder();
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"Project stats ({Path.GetFileName(path: project.ProjectPath)}): ");
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"sourceFiles={project.SourceFiles.Count} (with types={typesPerFile.Count(predicate: static count => count > 0)}, max types/file={(typesPerFile.Length == 0 ? 0 : typesPerFile[^1])}); ");
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"types total={types.Length} topLevel={topLevel} nested={types.Length - topLevel} [{string.Join(separator: ", ", values: byKind)}]; ");
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"accessibility [{string.Join(separator: ", ", values: byAccessibility)}]; ");
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"assemblies [{string.Join(separator: ", ", values: assemblies)}]; ");
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"delegates={project.Delegates.Count}; ");
        builder.Append(provider: CultureInfo.InvariantCulture, handler: $"properties={properties} (max/type={maxProperties}) methods={methods} (max/type={maxMethods}) parameters={parameters} fields={fields} constants={constants} enumValues={enumValues} attributes={attributes}");
        return builder.ToString();
    }

    public static string DescribeTemplates(IReadOnlyCollection<string> templatePaths) =>
        string.Create(
            provider: CultureInfo.InvariantCulture,
            handler: $"Templates: count={templatePaths.Count} [{string.Join(separator: ", ", values: templatePaths.Select(selector: Path.GetFileName))}]");

    public static string DescribeTemplateRun(
        string templatePath,
        string mode,
        int contextCount,
        int outputCount,
        TimeSpan totalRender,
        IReadOnlyList<(string File, TimeSpan Elapsed)> renders)
    {
        var slowest = renders
            .OrderByDescending(keySelector: static render => render.Elapsed)
            .Take(count: 5)
            .Select(selector: static render => string.Create(provider: CultureInfo.InvariantCulture, handler: $"{render.File}={render.Elapsed.TotalMilliseconds:F1}ms"));
        var average = renders.Count == 0 ? 0 : totalRender.TotalMilliseconds / renders.Count;
        return string.Create(
            provider: CultureInfo.InvariantCulture,
            handler: $"Template run ({Path.GetFileName(path: templatePath)}): mode={mode} renderCalls={contextCount} outputs={outputCount} totalRender={totalRender.TotalMilliseconds:F1}ms avg={average:F2}ms slowest [{string.Join(separator: ", ", values: slowest)}]");
    }
}
