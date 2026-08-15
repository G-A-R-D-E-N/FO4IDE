using System;
using System.Linq;
using System.Text;

namespace FO4RecordEditor.Services.Graph.F4SE;











public sealed class F4SEPluginEmitter
{
    public const string OgRuntimeVersion = "CURRENT_RELEASE_RUNTIME";

    public string EmitMain(PluginBinding plugin) =>
        plugin.Target == F4SETarget.Ng_0_7_8 ? EmitNgMain(plugin) : EmitOgMain(plugin);

    private string EmitOgMain(PluginBinding plugin)
    {
        var text = new StringBuilder();
        AppendCommonTop(text, plugin);

        text.Append("extern \"C\"\n{\n");
        text.Append("\tbool F4SEPlugin_Query(const F4SEInterface * f4se, PluginInfo * info)\n\t{\n");
        text.Append($"\t\tinfo->infoVersion = PluginInfo::kInfoVersion;\n");
        text.Append($"\t\tinfo->name = \"{plugin.Name}\";\n");
        text.Append($"\t\tinfo->version = {VersionLiteral(plugin)};\n\n");
        text.Append("\t\tg_pluginHandle = f4se->GetPluginHandle();\n\n");
        text.Append("\t\tif (f4se->isEditor)\n\t\t{\n");
        text.Append("\t\t\t_MESSAGE(\"loaded in editor, marking as incompatible\");\n");
        text.Append("\t\t\treturn false;\n\t\t}\n\n");
        text.Append($"\t\tif (f4se->runtimeVersion != {OgRuntimeVersion})\n\t\t{{\n");
        text.Append("\t\t\t_MESSAGE(\"unsupported runtime version %08X\", f4se->runtimeVersion);\n");
        text.Append("\t\t\treturn false;\n\t\t}\n\n");
        text.Append("\t\treturn true;\n\t}\n\n");

        AppendLoad(text, plugin);
        text.Append("}\n");
        return text.ToString();
    }

    private string EmitNgMain(PluginBinding plugin)
    {
        var text = new StringBuilder();
        AppendCommonTop(text, plugin);



        text.Append("extern \"C\"\n{\n");
        text.Append("\t__declspec(dllexport) F4SEPluginVersionData F4SEPlugin_Version =\n\t{\n");
        text.Append("\t\tF4SEPluginVersionData::kVersion,\n");
        text.Append($"\t\t{VersionLiteral(plugin)},\n");
        text.Append($"\t\t\"{plugin.Name}\",\n");
        text.Append($"\t\t\"{plugin.Author}\",\n");
        text.Append("\t\t\"\",\n");
        text.Append("\t\tF4SEPluginVersionData::kAddressIndependence_Signatures,\n");
        text.Append("\t\tF4SEPluginVersionData::kStructureIndependence_NoStructs,\n");
        text.Append("\t\t{ 0 },\n");
        text.Append("\t\t0,\n");
        text.Append("\t\t0, 0,\n");
        text.Append("\t\t{ 0 }\n");
        text.Append("\t};\n\n");

        AppendLoad(text, plugin);
        text.Append("}\n");
        return text.ToString();
    }

    private static void AppendCommonTop(StringBuilder text, PluginBinding plugin)
    {
        text.Append("#include \"f4se/PluginAPI.h\"\n");
        text.Append("#include \"f4se_common/f4se_version.h\"\n");
        text.Append("#include \"f4se/PapyrusVM.h\"\n\n");
        text.Append($"#include \"{plugin.Name}Registrations.h\"\n\n");
        text.Append("static PluginHandle g_pluginHandle = kPluginHandle_Invalid;\n");
        text.Append("static F4SEPapyrusInterface * g_papyrus = nullptr;\n");
        if (plugin.Messages.Count > 0)
            text.Append("static F4SEMessagingInterface * g_messaging = nullptr;\n");
        text.Append('\n');

        if (plugin.Messages.Count == 0) return;

        foreach (var subscription in plugin.Messages)
            text.Append($"void {subscription.HandlerName}(F4SEMessagingInterface::Message * message);\n");
        text.Append('\n');

        text.Append("static void OnF4SEMessage(F4SEMessagingInterface::Message * message)\n{\n");
        text.Append("\tswitch (message->type)\n\t{\n");
        foreach (var subscription in plugin.Messages)
        {
            text.Append($"\tcase F4SEMessagingInterface::{subscription.MessageName}:\n");
            text.Append($"\t\t{subscription.HandlerName}(message);\n");
            text.Append("\t\tbreak;\n");
        }
        text.Append("\tdefault:\n\t\tbreak;\n\t}\n}\n\n");
    }

    private static void AppendLoad(StringBuilder text, PluginBinding plugin)
    {
        text.Append("\tbool F4SEPlugin_Load(const F4SEInterface * f4se)\n\t{\n");
        text.Append("\t\tg_pluginHandle = f4se->GetPluginHandle();\n\n");
        text.Append("\t\tg_papyrus = (F4SEPapyrusInterface *)f4se->QueryInterface(kInterface_Papyrus);\n");
        text.Append("\t\tif (!g_papyrus || !g_papyrus->Register(RegisterAllFuncs))\n\t\t{\n");
        text.Append("\t\t\t_MESSAGE(\"could not register Papyrus functions\");\n");
        text.Append("\t\t\treturn false;\n\t\t}\n");

        if (plugin.Messages.Count > 0)
        {
            text.Append('\n');
            text.Append("\t\tg_messaging = (F4SEMessagingInterface *)f4se->QueryInterface(kInterface_Messaging);\n");
            text.Append("\t\tif (g_messaging)\n");
            text.Append("\t\t\tg_messaging->RegisterListener(g_pluginHandle, \"F4SE\", OnF4SEMessage);\n");
        }

        text.Append("\n\t\t");
        text.Append(F4SERegionMerge.Begin("F4SEPlugin_Load"));
        text.Append("\n\t\t");
        text.Append(F4SERegionMerge.End("F4SEPlugin_Load"));
        text.Append("\n\n\t\treturn true;\n\t}\n");
    }


    private static string VersionLiteral(PluginBinding plugin) =>
        $"MAKE_EXE_VERSION({plugin.VersionMajor}, {plugin.VersionMinor}, {plugin.VersionPatch})";










    public string EmitCMakeLists(PluginBinding plugin)
    {
        var sources = plugin.Modules
            .Select(m => "\tsrc/" + F4SECppEmitter.SourceFileName(plugin, m))
            .Prepend("\tsrc/main.cpp")
            .Append($"\tsrc/{plugin.Name}Registrations.cpp");

        var text = new StringBuilder();
        text.Append("cmake_minimum_required(VERSION 3.21)\n\n");
        text.Append($"project({plugin.Name} VERSION {plugin.VersionMajor}.{plugin.VersionMinor}.{plugin.VersionPatch} LANGUAGES CXX)\n\n");
        text.Append("set(CMAKE_CXX_STANDARD 17)\n");
        text.Append("set(CMAKE_CXX_STANDARD_REQUIRED ON)\n");
        text.Append("set(CMAKE_MSVC_RUNTIME_LIBRARY \"MultiThreaded$<$<CONFIG:Debug>:Debug>\")\n\n");
        text.Append("find_package(common REQUIRED CONFIG)\n");
        text.Append("find_package(f4se REQUIRED CONFIG)\n\n");
        text.Append($"add_library({plugin.Name} SHARED\n");
        text.Append(string.Join('\n', sources));
        text.Append("\n)\n\n");
        text.Append($"target_include_directories({plugin.Name} PRIVATE src)\n");
        text.Append($"target_link_libraries({plugin.Name} PRIVATE common::common f4se::f4se)\n");
        return text.ToString();
    }


    public string EmitReadme(PluginBinding plugin)
    {
        var target = plugin.Target == F4SETarget.Ng_0_7_8
            ? "F4SE 0.7.x and later. The plugin exports F4SEPluginVersionData."
            : "runtime 1.10.163. The plugin exports F4SEPlugin_Query.";

        var text = new StringBuilder();
        text.Append($"# {plugin.Name}\n\n");
        text.Append("Generated by FO4IDE from a binding graph. Regenerating overwrites every file\n");
        text.Append("here except the text between the body markers, which is preserved.\n\n");
        text.Append($"Target: {target}\n\n");
        text.Append("## Building\n\n");
        text.Append("F4SE builds as two installed packages. From a directory beside this one:\n\n");
        text.Append("```\n");
        text.Append("git clone https://github.com/ianpatt/common\n");
        text.Append("git clone https://github.com/ianpatt/f4se\n");
        text.Append("cmake -B common/build -S common -DCMAKE_INSTALL_PREFIX=extern\n");
        text.Append("cmake --build common/build --config Release --target install\n");
        text.Append("cmake -B f4se/build -S f4se -DCMAKE_INSTALL_PREFIX=extern -DCMAKE_PREFIX_PATH=extern\n");
        text.Append("cmake --build f4se/build --config Release --target install\n");
        text.Append("```\n\n");
        text.Append("Then build this plugin against that prefix:\n\n");
        text.Append("```\n");
        text.Append("cmake -B build -S . -DCMAKE_PREFIX_PATH=extern\n");
        text.Append("cmake --build build --config Release\n");
        text.Append("```\n\n");
        text.Append("## Function bodies\n\n");
        text.Append($"Every native is emitted as a stub guarded by `{F4SECppEmitter.UnimplementedMacro}`, which\n");
        text.Append("fails the build until an implementation is written. Write it between the\n");
        text.Append("`#pragma region FO4IDE_BODY` and `#pragma endregion FO4IDE_BODY` markers; that text survives regeneration. To build\n");
        text.Append($"with stubs still in place, define `{F4SECppEmitter.AllowStubsMacro}`.\n");
        return text.ToString();
    }
}
