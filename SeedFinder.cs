namespace XRL.SeedFinder {
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection.Emit;
    using System.Security.Cryptography;
    using AiUnity.Common.Extensions;
    using ConsoleLib.Console;
    using HarmonyLib;
    using HistoryKit;
    using XRL.Annals;
    using XRL.Core;
    using XRL.UI;
    using XRL.World;
    using XRL.World.Parts;

    public static class State {
        public static string Name = "Kizby"; // change this if you want
        public static string StartingLocation;

        public static string Seed;
        public static int LongestSeed = 6;

        // set to true for significantly faster iteration of seeds, though the greater world won't be
        // available for inspection and the game won't be in a playable state after
        public static bool StubWorldbuilding = true;

        // avoid using System.Random because it was maybe fucking up Qud's RNG?
        public static RNGCryptoServiceProvider random = new RNGCryptoServiceProvider();

        public static long InitialMemory = -1;
        public static bool Running;
        public static string Mode;
        public static string BuildCode;

        static State() {
            if (Environment.CommandLine.Contains("-continueSeedFinder")) {
                Running = true;
                foreach (var arg in Environment.GetCommandLineArgs()) {
                    if (arg.StartsWith("-mode")) {
                        Mode = Base64Decode(arg.After('=', false));
                    }
                    if (arg.StartsWith("-buildCode")) {
                        BuildCode = Base64Decode(arg.After('=', false));
                    }
                    if (arg.StartsWith("-name")) {
                        Name = Base64Decode(arg.After('=', false));
                    }
                    if (arg.StartsWith("-startingLocation")) {
                        StartingLocation = Base64Decode(arg.After('=', false));
                    }
                    if (arg.StartsWith("-seed")) {
                        StartingLocation = Base64Decode(arg.After('=', false));
                    }
                }
            }
        }
        public static bool Test() {
            var player = CreateCharacter.Template.PlayerBody;
            return player.Inventory.HasObject(o => o.GetLongProperty("Nanocrayons") == 1);
        }

        public static bool ShouldTryAgain() {
            if (!Test()) {
                Running = true;
                if (InitialMemory < 0) {
                    InitialMemory = GC.GetTotalMemory(false);
                } else if (GC.GetTotalMemory(false) > 2 * InitialMemory) {
                    Restart();
                }
                GameManager.Instance.PopGameView(); // clear away the WorldCreationProgress screen
                return true;
            }
            using (StreamWriter writer = new StreamWriter("seeds.txt", true)) {
                writer.WriteLine(Seed);
            }
            Running = false;
            return false;
        }

        // this implementation doesn't use any state from the current seed, but feel free to use
        // this parameter for e.g. sequential seeds
        public static string NextSeed(string _) {
            byte[] bytes = new byte[6];
            random.GetBytes(bytes);
            string result = "";
            for (int i = 0; i < bytes.Length; ++i) {
                // slight bias for some characters, but we don't really care
                result += "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[bytes[i] * 36 / 256];
            }
            if (result.Length > LongestSeed) {
                LongestSeed = result.Length;
            }
            return result;
        }

        public static void Restart() {
            Process[] pname = Process.GetProcessesByName("CoQ");
            if (pname.Length > 1) {
                // no
                return;
            }
            string commandLine = Environment.CommandLine;
            if (!commandLine.Contains("continueSeedFinder")) {
                commandLine += " -continueSeedFinder";
                // blech, not really a cleaner way to pass arbitrary names; may as well encode everything to futureproof
                commandLine += " -mode=" + Base64Encode(Mode);
                commandLine += " -buildCode=" + Base64Encode(BuildCode);
                commandLine += " -name=" + Base64Encode(Name);
                commandLine += " -startingLocation=" + Base64Encode(StartingLocation);
                commandLine += " -seed=" + Base64Encode(Seed);
            }
            if (Environment.OSVersion.Platform == PlatformID.Win32NT) {
                ProcessStartInfo Info = new ProcessStartInfo("cmd.exe", "/C ping 127.0.0.1 -n 2 && " + commandLine) {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                };
                _ = Process.Start(Info);
                Environment.FailFast("Too much memory :(");
            } else {
                // anyone on another OS is encouraged to implement it ♥
            }
        }
        public static string Base64Encode(string input) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(input));
        public static string Base64Decode(string input) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(input));
    }
    [HarmonyPatch(typeof(CreateCharacter), "PickGameMode")]
    public static class PatchPickGameMode {
        static bool Prefix(ref string __result) {
            if (State.Running) {
                __result = State.Mode;
                return false;
            }
            return true;
        }
        static void Postfix(ref string __result) {
            State.Mode = __result;
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "PickChargenType")]
    public static class PatchPickChargenType {
        static bool Prefix(ref string __result) {
            if (State.BuildCode != null) {
                __result = "<library>";
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "BuildLibraryManagement")]
    public static class PatchBuildLibraryManagement {
        static bool Prefix(ref string __result) {
            if (State.BuildCode != null) {
                __result = State.BuildCode;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "GenerateCharacter")]
    public static class PatchGenerateCharacter {
        static void Prefix() {
            The.Game.PlayerName = State.Name;
            The.Game.PlayerReputation.Init();
            CreateCharacter.WorldSeed = State.Seed = State.NextSeed(State.Seed);

        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            bool Patched = false;
            foreach (var inst in instructions) {
                // skip everything starting with generating the character code, it's unneeded
                if (!Patched && inst.Is(OpCodes.Ldsfld, AccessTools.Field(typeof(CreateCharacter), "Code"))) {
                    yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(State), "Running"));
                    Label label = generator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Brfalse, label);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Ret);
                    yield return new CodeInstruction(inst).WithLabels(label);
                    Patched = true;
                } else {
                    yield return inst;
                }
            }
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "Embark")]
    public static class PatchEmbark {
        static bool Prefix(ref bool __result) {
            if (State.BuildCode == null) {
                State.BuildCode = CreateCharacter.Code.ToString().ToUpper();
            }
            if (State.StartingLocation != null) {
                The.Game.SetStringGameState("embark", State.StartingLocation);

                __result = true;
                return false;
            }
            return true;
        }
        static void Postfix() {
            if (State.StartingLocation == null) {
                State.StartingLocation = The.Game.GetStringGameState("embark");
            }
        }
    }
    [HarmonyPatch(typeof(XRLCore), "NewGame")]
    public static class PatchNewGame {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            var start = generator.DefineLabel();
            var createCharacterLabel = generator.DefineLabel();
            bool first = true;
            foreach (var inst in instructions) {
                var actual = inst;
                if (first) {
                    // add a "start" label to the first instruction
                    actual = new CodeInstruction(inst);
                    actual.labels.Add(start);
                    first = false;
                }
                if (inst.Is(OpCodes.Call, AccessTools.Method(typeof(CreateCharacter), "ShowCreateCharacter"))) {
                    actual = new CodeInstruction(inst);
                    actual.labels.Add(createCharacterLabel);
                }
                yield return actual;
                if (State.StubWorldbuilding ?
                        inst.Is(OpCodes.Call, AccessTools.Method(typeof(CreateCharacter), "GenerateEquipment")) :
                        inst.Is(OpCodes.Call, AccessTools.Method(typeof(GameObject), "UpdateVisibleStatusColor"))) {
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(State), "ShouldTryAgain"));
                    // try again instead of returning at the end if ShouldTryAgain tells us to
                    // if we didn't stub the world building, we need to go all the way to the start to reset the necessary state
                    yield return new CodeInstruction(OpCodes.Brtrue, State.StubWorldbuilding ? createCharacterLabel : start);
                }
            }
        }
    }
    [HarmonyPatch(typeof(XRLCore), "_Start")]
    public static class PatchStart {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            foreach (var inst in instructions) {
                // if we've just restarted (can tell from command line), go right into new game workflow
                if (inst.Is(OpCodes.Call, AccessTools.Method(typeof(XRLCore), "LoadEverything"))) {
                    yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(State), "Running"));
                    Label label = generator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Brfalse, label);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(XRLCore), "NewGame"));
                    yield return new CodeInstruction(inst).WithLabels(label);
                } else {
                    yield return inst;
                }
            }
        }
    }
    [HarmonyPatch(typeof(WorldCreationProgress), "Draw")]
    public static class PatchDraw {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            bool replaceNext = false;
            bool replaced = false;
            foreach (var inst in instructions) {
                if (!replaced && replaceNext) {
                    // instead of popping, call our function on the returned screen buffer
                    yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(PatchDraw), "AddToScreenBuffer"));
                    replaced = true;
                } else {
                    yield return inst;
                }
                if (!replaced && inst.Is(OpCodes.Callvirt, AccessTools.Method(typeof(ScreenBuffer), "Write", new Type[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) }))) {
                    // take the output of this call to write
                    replaceNext = true;
                }
            }
        }

        static void AddToScreenBuffer(ScreenBuffer screenBuffer) {
            var _ = screenBuffer.Goto(5, 22)
                                .Write("Seed: " + XRLCore.Core.Game.GetStringGameState("OriginalWorldSeed"))
                                .Goto(15 + State.LongestSeed, 22)
                                .Write("Memory: " + GC.GetTotalMemory(false))
                                .Goto(35 + State.LongestSeed, 22)
                                .Write("Build Code: " + State.BuildCode);
        }
    }
    [HarmonyPatch(typeof(QudHistoryFactory), "GenerateNewSultanHistory")]
    public static class PatchGenerateNewSultanHistory {
        static bool Prefix(ref History __result) {
            if (!State.StubWorldbuilding) {
                return true;
            }
            __result = new History(1);
            return false;
        }
    }
    [HarmonyPatch(typeof(ModEngraved), "GenerateEngraving")]
    public static class PatchGenerateEngraving {
        static void Prefix(ref ModEngraved __instance) {
            if (State.StubWorldbuilding) {
                // no gods, no masters, so we need something to engrave on shit
                __instance.Sultan = "unormal";
                __instance.Engraving = "Ceci n'est pas une " + __instance.ParentObject.ShortDisplayNameSingle;
            }
        }
    }
    [HarmonyPatch(typeof(QudHistoryFactory), "GenerateVillageEraHistory")]
    public static class PatchGenerateVillageEraHistory {
        static bool Prefix(History history, ref History __result) {
            if (!State.StubWorldbuilding) {
                return true;
            }
            __result = history;
            return false;
        }
    }
    [HarmonyPatch(typeof(WorldFactory), "BuildZoneNameMap")]
    public static class PatchBuildZoneNameMap {
        static bool Prefix() {
            if (!State.StubWorldbuilding) {
                return true;
            }
            WorldFactory.Factory.ZoneIDToDisplay = new Dictionary<string, string>();
            WorldFactory.Factory.ZoneDisplayToID = new Dictionary<string, string>();
            return false;
        }
    }
    [HarmonyPatch(typeof(WorldFactory), "BuildWorlds")]
    public static class PatchBuildWorlds {
        static bool Prefix() {
            return !State.StubWorldbuilding;
        }
    }
}
