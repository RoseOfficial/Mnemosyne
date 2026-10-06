using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Mnemosyne.Assembly.Tests;

/// <summary>The DLL cannot make a network call because it does not contain the code to. Core references no game library. The
/// switch's one command is the only thing Mnemosyne hands another plugin, checked by walking every method body. All are
/// checked on the built files, by metadata, without loading them.</summary>
public class AssemblyTests
{
    [Theory]
    [InlineData("Mnemosyne.Core.dll")]
    [InlineData("Mnemosyne.Interop.dll")]
    [InlineData("Mnemosyne.dll")]
    public void The_built_assembly_references_nothing_that_can_reach_a_network(string fileName)
    {
        var (path, assemblies, namespaces) = References(fileName);
        var offenders = assemblies.Where(IsNetwork).Select(a => "assembly " + a)
            .Concat(namespaces.Where(IsNetwork).Select(n => "namespace " + n))
            .ToList();
        Assert.True(offenders.Count == 0, $"{path} can reach the network through: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Core_references_no_game_library()
    {
        var (path, assemblies, _) = References("Mnemosyne.Core.dll");
        var offenders = assemblies.Where(a => a.StartsWith("Dalamud", StringComparison.Ordinal)
                                           || a.StartsWith("Lumina", StringComparison.Ordinal)
                                           || a.StartsWith("FFXIVClientStructs", StringComparison.Ordinal)).ToList();
        Assert.True(offenders.Count == 0, $"{path} references game libraries: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Only_the_switch_dispatches_a_command()
    {
        // Every call, delegate load and construction in the three built assemblies is resolved by name, and the members that
        // reach another plugin are held to their one allowed caller, or to none.
        var calls = new[] { "Mnemosyne.Core.dll", "Mnemosyne.Interop.dll", "Mnemosyne.dll" }.SelectMany(file => Calls(Newest(file))).ToList();
        string[] CallersOf(params string[] members) => calls.Where(c => members.Contains(c.Callee)).Select(c => c.Caller).Distinct().Order().ToArray();

        var dispatchers = CallersOf("Dalamud.Plugin.Services.ICommandManager.ProcessCommand");
        Assert.True(dispatchers.SequenceEqual(["Mnemosyne.Interop.RelogService.SwitchTo"]),
            $"ICommandManager.ProcessCommand must be called from RelogService.SwitchTo alone; it is called from: {string.Join(", ", dispatchers)}");
        var forbidden = CallersOf(
            "Dalamud.Plugin.Services.ICommandManager.DispatchCommand",
            "Dalamud.Plugin.IDalamudPluginInterface.GetIpcSubscriber",
            "Dalamud.Plugin.IDalamudPluginInterface.GetIpcProvider",
            "Dalamud.Plugin.IExposedPlugin.OpenMainUi",
            "Dalamud.Plugin.IExposedPlugin.OpenConfigUi",
            // Two other ways to run a command, left unguarded by the caller check above. RelogService already reads
            // ICommandManager.Commands, so a later change could invoke a handler's delegate directly instead of going through
            // ProcessCommand; and the game's own chat entry points are reachable through FFXIVClientStructs. Nothing calls
            // any of these today.
            "Dalamud.Game.Command.IReadOnlyCommandInfo.get_Handler",
            "FFXIVClientStructs.FFXIV.Client.UI.UIModule.ProcessChatBoxEntry",
            "FFXIVClientStructs.FFXIV.Component.Shell.ShellCommandModule.ExecuteCommandInner",
            "FFXIVClientStructs.FFXIV.Client.UI.Shell.RaptureShellModule.ExecuteCommandInner");
        Assert.True(forbidden.Length == 0, $"a member that reaches another plugin is called from: {string.Join(", ", forbidden)}");
    }

    [Fact]
    public void Information_is_logged_only_at_load_and_for_the_players_own_export_and_import()
    {
        // What a reader saw, a content id, a path or a name is logged at Debug, and a fault at Warning or Error. Information, which the log
        // shows by default, carries the one line at load and the two lines that confirm the player's own export and import, none of which
        // names a character.
        var calls = new[] { "Mnemosyne.Core.dll", "Mnemosyne.Interop.dll", "Mnemosyne.dll" }.SelectMany(file => Calls(Newest(file))).ToList();
        var callers = calls.Where(c => c.Callee is "Dalamud.Plugin.Services.IPluginLog.Information" or "Dalamud.Plugin.Services.IPluginLog.Info")
            .Select(c => c.Caller).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Mnemosyne.Plugin..ctor", "Mnemosyne.Transfer.Apply", "Mnemosyne.Transfer.Export"], callers);
    }

    private static bool IsNetwork(string name) =>
        name.StartsWith("System.Net", StringComparison.Ordinal) || name.StartsWith("Dalamud.Networking", StringComparison.Ordinal);

    private static (string Path, List<string> Assemblies, List<string> Namespaces) References(string fileName)
    {
        var path = Newest(fileName);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var assemblies = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).Distinct().ToList();
        var namespaces = md.TypeReferences.Select(h => md.GetString(md.GetTypeReference(h).Namespace)).Distinct().ToList();
        return (path, assemblies, namespaces);
    }

    /// <summary>Every IL opcode by its value, one byte, or 0xFE and one byte, so each operand can be stepped over by its size.</summary>
    private static readonly Dictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => (ushort)code.Value)
        .ToDictionary(group => group.Key, group => group.First());

    /// <summary>Every (caller, callee) pair in one assembly whose callee is a member of another assembly: each method body's call, callvirt,
    /// newobj, ldftn and ldvirtftn whose token is a member reference, or a generic instantiation of one. Names are Namespace.Type.Member,
    /// with nested types joined by '+'.</summary>
    private static List<(string Caller, string Callee)> Calls(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var found = new List<(string, string)>();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var typeName = TypeName(md, typeHandle);
            foreach (var methodHandle in md.GetTypeDefinition(typeHandle).GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0) continue;   // abstract, extern or an interface member: no body
                var caller = typeName + "." + md.GetString(method.Name);
                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                while (il.RemainingBytes > 0)
                {
                    int value = il.ReadByte();
                    if (value == 0xFE) value = 0xFE00 | il.ReadByte();
                    switch (OpCodesByValue[(ushort)value].OperandType)
                    {
                        case OperandType.InlineNone:
                            break;
                        case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                            il.Offset += 1;
                            break;
                        case OperandType.InlineVar:
                            il.Offset += 2;
                            break;
                        case OperandType.InlineI8 or OperandType.InlineR:
                            il.Offset += 8;
                            break;
                        case OperandType.InlineSwitch:
                            // The count first, then the offset: "il.Offset += 4 * il.ReadInt32()" would read Offset before ReadInt32 moved
                            // past the count, and land four bytes short of the next instruction.
                            var targets = il.ReadInt32();
                            il.Offset += 4 * targets;
                            break;
                        case OperandType.InlineMethod:
                            if (Callee(md, MetadataTokens.EntityHandle(il.ReadInt32())) is { } callee) found.Add((caller, callee));
                            break;
                        default:   // a long branch, a field, type, string, signature or token, an int32 or a float32
                            il.Offset += 4;
                            break;
                    }
                }
            }
        }
        return found;
    }

    /// <summary>The called member's full name when it belongs to another assembly; null for a method of this one.</summary>
    private static string? Callee(MetadataReader md, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodSpecification) handle = md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind != HandleKind.MemberReference) return null;
        var member = md.GetMemberReference((MemberReferenceHandle)handle);
        return member.Parent.Kind == HandleKind.TypeReference ? TypeName(md, (TypeReferenceHandle)member.Parent) + "." + md.GetString(member.Name) : null;
    }

    private static string TypeName(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var name = md.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return TypeName(md, declaring) + "+" + name;
        var ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string TypeName(MetadataReader md, TypeReferenceHandle handle)
    {
        var type = md.GetTypeReference(handle);
        var name = md.GetString(type.Name);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference) return TypeName(md, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
        var ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>The newest build of the named assembly for this test project's own configuration. The project reference forces
    /// that configuration to build first, so a stale build of the other configuration can never be the one under test.</summary>
    internal static string Newest(string fileName)
    {
        var configuration = typeof(AssemblyTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Release";
        var src = Path.Combine(RepoRoot(), "src");
        var sep = Path.DirectorySeparatorChar;
        var candidates = Directory.EnumerateFiles(src, fileName, SearchOption.AllDirectories)
            .Where(p => !p.Contains(sep + "obj" + sep, StringComparison.OrdinalIgnoreCase))
            .Where(p => p.Contains(sep + configuration + sep, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        Assert.True(candidates.Count > 0, $"{fileName} was not found under {src} for the {configuration} configuration; build the solution in that configuration first");
        return candidates[0];
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mnemosyne.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Mnemosyne.sln was not found above the test directory");
    }
}
