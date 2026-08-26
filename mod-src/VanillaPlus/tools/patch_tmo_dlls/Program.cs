using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Rewrites the IModApi.InitMod body of the TheMeanOne DLLs to remove their
// "TMO Core required" gate. The patched flow is identical except it no longer
// checks ModManager.GetMod("TMO Core") and instead always proceeds:
//     load config from mod path  ->  new Harmony(id).PatchAll(assembly)
//
// Usage: patch_tmo_dlls <modRoot>     (writes patched DLLs into <modRoot>/mod/)
class Program
{
    static string MANAGED = "/srv/7days/7DaysToDieServer_Data/Managed";
    static string HARMONY = "/srv/7days/Mods/0_TFP_Harmony/0Harmony.dll";

    static AssemblyNameReference AsmRef(ModuleDefinition module, string name)
        => module.AssemblyReferences.First(a => a.Name == name);

    static void Patch(string src, string dst, string configCallType, string configCallMethod, string harmonyId)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(MANAGED);
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(HARMONY)));
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(src)));

        var asm = AssemblyDefinition.ReadAssembly(Path.GetFullPath(src), new ReaderParameters
        {
            AssemblyResolver = resolver,
            InMemory = true,
        });
        var module = asm.MainModule;

        MethodDefinition init = null;
        foreach (var t in module.Types)
            foreach (var m in t.Methods)
                if (m.Name == "InitMod" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "Mod")
                { init = m; break; }
        if (init == null) throw new Exception($"{src}: InitMod not found");

        var configType = module.Types.First(t => t.Name == configCallType);
        var loadMethod = configType.Methods.First(m => m.Name == configCallMethod && m.Parameters.Count == 1 &&
                                                      m.Parameters[0].ParameterType.FullName == "System.String");

        var modRef = new TypeReference("", "Mod", module, AsmRef(module, "Assembly-CSharp"));
        var getPath = modRef.Resolve().Methods.First(m => m.Name == "get_Path" && m.Parameters.Count == 0);

        var harmRef = new TypeReference("HarmonyLib", "Harmony", module, AsmRef(module, "0Harmony"));
        var harmonyCtor = harmRef.Resolve().Methods.First(m => m.IsConstructor && m.Parameters.Count == 1 &&
                                                               m.Parameters[0].ParameterType.FullName == "System.String");
        var patchAll = harmRef.Resolve().Methods.First(m => m.Name == "PatchAll" && m.Parameters.Count == 1);

        var getExecAsm = module.ImportReference(typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly"));

        var il = init.Body.GetILProcessor();
        init.Body.Instructions.Clear();
        init.Body.ExceptionHandlers.Clear();
        init.Body.Variables.Clear();
        init.Body.InitLocals = false;

        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(getPath)));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(loadMethod)));
        il.Append(il.Create(OpCodes.Ldstr, harmonyId));
        il.Append(il.Create(OpCodes.Newobj, module.ImportReference(harmonyCtor)));
        il.Append(il.Create(OpCodes.Call, getExecAsm));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(patchAll)));
        il.Append(il.Create(OpCodes.Ret));

        asm.Write(Path.GetFullPath(dst));
        Console.WriteLine($"  patched {Path.GetFileName(src)} -> {Path.GetFileName(dst)}");
    }

    static void Main(string[] args)
    {
        if (args.Length < 1) throw new Exception("usage: patch_tmo_dlls <modRoot>");
        string root = Path.GetFullPath(args[0]);
        string srcDir = Path.Combine(root, "src", "original");
        string outDir = Path.Combine(root, "mod");

        Patch(Path.Combine(srcDir, "TheMeanOnes_ZombiesDontDig.dll"),
              Path.Combine(outDir, "TheMeanOnes_ZombiesDontDig.dll"),
              "DigConfig", "Load", "VanillaPlus.ZombiesCantDig");
        Patch(Path.Combine(srcDir, "TheMeanOnes_AirDropsPlus.dll"),
              Path.Combine(outDir, "TheMeanOnes_AirDropsPlus.dll"),
              "SupplyManagerConfig", "LoadOrCreateConfig", "VanillaPlus.AirDropsPlus");
    }
}
