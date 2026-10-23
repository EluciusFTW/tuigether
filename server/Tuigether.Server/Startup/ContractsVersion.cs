using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tuigether.Contracts;

namespace Tuigether.Server.Startup;

/// <summary>
/// A fingerprint of every public type in the contracts assembly. An additive change of a contract also changes it.
/// A node with older contracts cannot read the events of a newer node.
/// </summary>
public static class ContractsVersion
{
    public static string Current { get; } = Of(typeof(Protocol).Assembly);

    public static string Of(Assembly contracts)
    {
        var lines = contracts
            .GetExportedTypes()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .SelectMany(Describe);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexStringLower(hash)[..16];
    }

    private static IEnumerable<string> Describe(Type type) =>
        [
            type.FullName ?? type.Name,
            .. type.IsEnum
                ? Enum.GetNames(type).AsEnumerable()
                : type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(static property => $"{property.Name}: {property.PropertyType}")
                    .Concat(
                        type.GetFields(BindingFlags.Public | BindingFlags.Static)
                            .Where(static field => field.IsLiteral)
                            .Select(static field => $"{field.Name} = {field.GetRawConstantValue()}"))
                    .Order(StringComparer.Ordinal),
        ];
}

/// <summary>
/// The contracts version of the nodes that use this database. The first node writes it.
/// </summary>
public sealed record ContractsVersionDocument(string Id, string Version)
{
    public const string TheId = "contracts";
}

public static class ContractsVersionRules
{
    public static Result<string, string> Check(string stored, string current) =>
        stored == current
            ? Result.Ok(current)
            : Result.Error(
                $"This node runs contracts version {current}, and the database holds version {stored}. "
                + "Run all nodes on one database with the same version. To change the version, stop all nodes, "
                + $"delete the document '{ContractsVersionDocument.TheId}' of type {nameof(ContractsVersionDocument)}, and start the new nodes.");
}
