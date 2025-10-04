using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var arguments = ParseArgs(args);
if (!arguments.TryGetValue("assembly", out var assemblyPath) || string.IsNullOrEmpty(assemblyPath))
{
    Console.Error.WriteLine("Missing --assembly <path> argument.");
    PrintUsage();
    return 1;
}

assemblyPath = Path.GetFullPath(assemblyPath);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
    return 1;
}

var signatures = ExtractSignatures(assemblyPath).ToImmutableSortedSet(StringComparer.Ordinal);

if (arguments.ContainsKey("generate"))
{
    if (!arguments.TryGetValue("baseline", out var baselinePath) || string.IsNullOrEmpty(baselinePath))
    {
        Console.Error.WriteLine("Missing --baseline <path> for generation mode.");
        return 1;
    }

    baselinePath = Path.GetFullPath(baselinePath);
    Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
    File.WriteAllLines(baselinePath, signatures);
    Console.WriteLine($"Baseline written to {baselinePath} (entries: {signatures.Count}).");
    return 0;
}
else
{
    if (!arguments.TryGetValue("baseline", out var baselinePath) || string.IsNullOrEmpty(baselinePath))
    {
        Console.Error.WriteLine("Missing --baseline <path> for verification mode.");
        PrintUsage();
        return 1;
    }

    baselinePath = Path.GetFullPath(baselinePath);
    if (!File.Exists(baselinePath))
    {
        Console.Error.WriteLine($"Baseline not found: {baselinePath}");
        return 1;
    }

    var baseline = File.ReadAllLines(baselinePath)
                       .Where(line => !string.IsNullOrWhiteSpace(line))
                       .ToImmutableHashSet(StringComparer.Ordinal);

    var missing = baseline.Except(signatures).ToList();
    if (missing.Count > 0)
    {
        Console.Error.WriteLine("Detected breaking changes in public interfaces:");
        foreach (var entry in missing.OrderBy(s => s, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"  MISSING: {entry}");
        }
        Console.Error.WriteLine("Additive changes are allowed, but removals or signature changes must be avoided.");
        return 2;
    }

    Console.WriteLine($"Interface baseline validated ({signatures.Count} signatures).");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("Interface API Check");
    Console.WriteLine("Usage:");
    Console.WriteLine("  interface-api-check --generate --baseline <file> --assembly <path>");
    Console.WriteLine("  interface-api-check --verify --baseline <file> --assembly <path>");
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];
        if (!current.StartsWith("--", StringComparison.Ordinal))
        {
            continue;
        }

        var key = current[2..];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            dict[key] = args[++i];
        }
        else
        {
            dict[key] = "true";
        }
    }

    return dict;
}

static IEnumerable<string> ExtractSignatures(string assemblyPath)
{
    using var stream = File.OpenRead(assemblyPath);
    using var peReader = new PEReader(stream);
    var reader = peReader.GetMetadataReader();
    var provider = new TypeNameProvider(reader);

    foreach (var handle in reader.TypeDefinitions)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        if (!IsPublicInterface(typeDef))
        {
            continue;
        }

        var interfaceName = provider.GetTypeDefinitionDisplayName(handle);
        var typeGenericParams = typeDef.GetGenericParameters()
                                       .Select(p => reader.GetString(reader.GetGenericParameter(p).Name))
                                       .ToArray();

        var baseInterfaces = typeDef.GetInterfaceImplementations()
                                    .Select(impl => provider.GetTypeDisplayName(reader.GetInterfaceImplementation(impl).Interface, typeGenericParams))
                                    .OrderBy(n => n, StringComparer.Ordinal)
                                    .ToArray();
        var header = baseInterfaces.Length > 0
            ? $"interface {interfaceName} : {string.Join(", ", baseInterfaces)}"
            : $"interface {interfaceName}";
        yield return header;

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.SpecialName) != 0)
            {
                continue; // Skip property/event accessors
            }

            var methodName = reader.GetString(method.Name);
            var methodGenericParams = method.GetGenericParameters()
                                            .Select(p => reader.GetString(reader.GetGenericParameter(p).Name))
                                            .ToArray();

            var signature = method.DecodeSignature(provider, new GenericContext(typeGenericParams, methodGenericParams));
            var genericSuffix = methodGenericParams.Length > 0
                ? $"<{string.Join(", ", methodGenericParams)}>"
                : string.Empty;
            var parameters = string.Join(", ", signature.ParameterTypes);
            var line = $"method {interfaceName}::{methodName}{genericSuffix}({parameters}) -> {signature.ReturnType}";
            yield return line;
        }

        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var propertyName = reader.GetString(property.Name);
            var signature = property.DecodeSignature(provider, new GenericContext(typeGenericParams, Array.Empty<string>()));
            var accessor = property.GetAccessors();
            var accessors = new List<string>(2);
            if (!accessor.Getter.IsNil)
            {
                accessors.Add("get;");
            }
            if (!accessor.Setter.IsNil)
            {
                accessors.Add("set;");
            }
            var accessorText = accessors.Count > 0 ? string.Join(" ", accessors) : string.Empty;
            var parameters = signature.ParameterTypes.Length > 0
                ? $"[{string.Join(", ", signature.ParameterTypes)}]"
                : string.Empty;
            var line = parameters.Length > 0
                ? $"property {interfaceName}::{propertyName}{parameters} : {signature.ReturnType} {{ {accessorText} }}"
                : $"property {interfaceName}::{propertyName} : {signature.ReturnType} {{ {accessorText} }}";
            yield return line.TrimEnd();
        }

        foreach (var eventHandle in typeDef.GetEvents())
        {
            var eventDef = reader.GetEventDefinition(eventHandle);
            var eventName = reader.GetString(eventDef.Name);
            var eventType = provider.GetTypeDisplayName(eventDef.Type, typeGenericParams);
            yield return $"event {interfaceName}::{eventName} : {eventType}";
        }

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.SpecialName) != 0)
            {
                continue;
            }

            var fieldName = reader.GetString(field.Name);
            var fieldType = field.DecodeSignature(provider, new GenericContext(typeGenericParams, Array.Empty<string>()));
            var kind = (field.Attributes & FieldAttributes.Literal) != 0 ? "const" : "field";
            yield return $"{kind} {interfaceName}::{fieldName} : {fieldType}";
        }
    }
}

static bool IsPublicInterface(TypeDefinition typeDefinition)
{
    var attrs = typeDefinition.Attributes;
    var visibility = attrs & TypeAttributes.VisibilityMask;
    var isPublic = visibility == TypeAttributes.Public || visibility == TypeAttributes.NestedPublic;
    var isInterface = (attrs & TypeAttributes.Interface) != 0;
    return isPublic && isInterface;
}

readonly struct GenericContext
{
    public GenericContext(string[] typeParameters, string[] methodParameters)
    {
        TypeParameters = typeParameters;
        MethodParameters = methodParameters;
    }

    public string[] TypeParameters { get; }
    public string[] MethodParameters { get; }
}

sealed class TypeNameProvider : ISignatureTypeProvider<string, GenericContext>
{
    private readonly MetadataReader _reader;

    public TypeNameProvider(MetadataReader reader)
    {
        _reader = reader;
    }

    public string GetArrayType(string elementType, ArrayShape shape)
    {
        if (shape.Rank == 1 && shape.LowerBounds.Length == 0 && shape.Sizes.Length == 0)
        {
            return elementType + "[]";
        }

        var commas = new string(',', Math.Max(0, (int)shape.Rank - 1));
        return $"{elementType}[{commas}]";
    }

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature)
    {
        var parameters = string.Join(", ", signature.ParameterTypes);
        return "delegate* (" + parameters + ") -> " + signature.ReturnType;
    }

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        return genericType + "<" + string.Join(", ", typeArguments) + ">";
    }

    public string GetGenericMethodParameter(GenericContext genericContext, int index)
    {
        return genericContext.MethodParameters.Length > index
            ? genericContext.MethodParameters[index]
            : $"!!{index}";
    }

    public string GetGenericTypeParameter(GenericContext genericContext, int index)
    {
        return genericContext.TypeParameters.Length > index
            ? genericContext.TypeParameters[index]
            : $"!{index}";
    }

    public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired)
    {
        var prefix = isRequired ? "modreq" : "modopt";
        return $"{unmodifiedType} {prefix}({modifierType})";
    }

    public string GetPinnedType(string elementType) => elementType + " pinned";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Void => "void",
        _ => typeCode.ToString()
    };

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        return GetTypeDefinitionDisplayName(handle);
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var typeRef = reader.GetTypeReference(handle);
        var name = reader.GetString(typeRef.Name);
        var ns = typeRef.Namespace.IsNil ? string.Empty : reader.GetString(typeRef.Namespace);
        var declaring = typeRef.ResolutionScope.Kind switch
        {
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)typeRef.ResolutionScope, rawTypeKind),
            HandleKind.TypeDefinition => GetTypeDefinitionDisplayName((TypeDefinitionHandle)typeRef.ResolutionScope),
            _ => null
        };
        var fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        if (declaring != null)
        {
            fullName = declaring + "." + name;
        }
        return fullName;
    }

    public string GetTypeFromSpecification(MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var specification = reader.GetTypeSpecification(handle);
        return specification.DecodeSignature(this, genericContext);
    }

    public string GetTypeDisplayName(EntityHandle handle, string[] typeParameterContext)
    {
        var context = new GenericContext(typeParameterContext, Array.Empty<string>());
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeDefinitionDisplayName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(_reader, context, (TypeSpecificationHandle)handle, 0),
            _ => handle.Kind.ToString()
        };
    }

    public string GetTypeDefinitionDisplayName(TypeDefinitionHandle handle)
    {
        var typeDef = _reader.GetTypeDefinition(handle);
        var name = _reader.GetString(typeDef.Name);
        var ns = typeDef.Namespace.IsNil ? string.Empty : _reader.GetString(typeDef.Namespace);
        if (!typeDef.GetDeclaringType().IsNil)
        {
            var declaring = GetTypeDefinitionDisplayName(typeDef.GetDeclaringType());
            name = declaring + "." + name;
            ns = string.Empty;
        }

        var genericParameters = typeDef.GetGenericParameters()
                                       .Select(p => _reader.GetString(_reader.GetGenericParameter(p).Name))
                                       .ToArray();
        if (genericParameters.Length > 0)
        {
            name += "<" + string.Join(", ", genericParameters) + ">";
        }

        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
