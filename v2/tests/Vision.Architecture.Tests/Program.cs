using System.Reflection;
using VisionSystem.Application;
using VisionSystem.Domain;

namespace VisionSystem.Architecture.Tests;

public static class Program
{
    public static int Main()
    {
        var domain = typeof(Pose2D).Assembly;
        var application = typeof(InspectionOrchestrator).Assembly;

        AssertNoReferences(domain,
            "VisionSystem.Application",
            "VisionSystem.Infrastructure",
            "VisionSystem.Halcon",
            "halcondotnet",
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase");
        Console.WriteLine("PASS Domain is independent of application, HALCON, WPF and infrastructure");

        AssertNoReferences(application,
            "VisionSystem.Infrastructure",
            "VisionSystem.Halcon",
            "halcondotnet",
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase");
        AssertReferences(application, "VisionSystem.Domain");
        Console.WriteLine("PASS Application depends on Domain but not HALCON, WPF or infrastructure");

        AssertPublicApiDoesNotExposeNamespace(domain, "HalconDotNet");
        AssertPublicApiDoesNotExposeNamespace(application, "HalconDotNet");
        Console.WriteLine("PASS Core public APIs do not leak HalconDotNet types");

        Console.WriteLine("Architecture tests passed: 3/3");
        return 0;
    }

    private static void AssertNoReferences(Assembly assembly, params string[] forbidden)
    {
        var references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var violations = forbidden.Where(references.Contains).ToArray();
        if (violations.Length > 0)
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} has forbidden references: {string.Join(", ", violations)}.");
        }
    }

    private static void AssertReferences(Assembly assembly, string required)
    {
        if (assembly.GetReferencedAssemblies().All(reference =>
                !string.Equals(reference.Name, required, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} must reference {required}.");
        }
    }

    private static void AssertPublicApiDoesNotExposeNamespace(Assembly assembly, string forbiddenNamespace)
    {
        foreach (var type in assembly.ExportedTypes)
        {
            var exposedTypes = type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                .Select(property => property.PropertyType)
                .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
                    .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType)));
            var violation = exposedTypes.FirstOrDefault(exposed =>
                ContainsNamespace(exposed, forbiddenNamespace, new HashSet<Type>()));
            if (violation is not null)
            {
                throw new InvalidOperationException(
                    $"Public type {type.FullName} exposes forbidden API type {violation.FullName}.");
            }
        }
    }

    private static bool ContainsNamespace(Type type, string forbiddenNamespace, ISet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (string.Equals(type.Namespace, forbiddenNamespace, StringComparison.Ordinal) ||
            type.Namespace?.StartsWith(forbiddenNamespace + ".", StringComparison.Ordinal) == true)
        {
            return true;
        }

        if (type.HasElementType && type.GetElementType() is { } element &&
            ContainsNamespace(element, forbiddenNamespace, visited))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments()
            .Any(argument => ContainsNamespace(argument, forbiddenNamespace, visited));
    }
}
