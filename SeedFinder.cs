namespace XRL.SeedFinder {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection.Emit;
    using AiUnity.Common.Extensions;
    using ConsoleLib.Console;
    using HarmonyLib;
    using XRL.Annals;
    using XRL.Core;
    using XRL.UI;
    using XRL.World;

    public static class State {
        public const string Name = "Kizby"; // change this if you want
        public const int StartingLocation = 0; // {Joppa, marsh, dunes, canyon, hills}

        public static string Seed;
        public static int SeedLength = 8;

        // set to true for significantly faster iteration of seeds, though the greater world won't be
        // available for inspection and the game won't be in a playable state after
        public static bool StubWorldbuilding = true;

        public static Random random = new Random();
        public static bool Test() {
            var player = CreateCharacter.Template.PlayerBody;
            return player.Inventory.HasObject(o => o.GetLongProperty("Nanocrayons") == 1);
        }

        public static bool ShouldTryAgain() {
            if (!Test()) {
                Seed = NextSeed();
                GameManager.Instance.PopGameView(); // clear away the WorldCreationProgress screen
                return true;
            }
            using (StreamWriter writer = new StreamWriter("seeds.txt", true)) {
                writer.WriteLine(Seed);
            }
            return false;
        }

        public static string NextSeed() {
            string result = "";
            while (result.Length < SeedLength) {
                result += "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[random.Next(36)];
            }
            return result;
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "PickGameType")]
    public static class PatchPickGameType {
        static bool Prefix(ref string __result) {
            XRLCore.Core.Game.PlayerName = State.Name;
            __result = "<manualseed>";
            return false;
        }
    }
    [HarmonyPatch(typeof(Popup), "AskString")]
    public static class PatchAskString {
        static bool Prefix(ref string Message, ref string __result) {
            if (Message == "Enter a world seed.") {
                State.Seed = State.NextSeed();
                __result = State.Seed;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "GenerateCharacter")]
    public static class PatchCreateCharacter {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
            foreach (var inst in instructions) {
                if (inst.Is(OpCodes.Ldsfld, AccessTools.Field(typeof(CreateCharacter), "Code"))) {
                    // skip everything starting with generating the character code, it's unneeded
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Ret);
                    break;
                }
                yield return inst;
            }
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "Embark")]
    public static class PatchEmbark {
        static bool Prefix(ref bool __result) {
            List<string> list = new List<string>{
                "&YJoppa",
                "&YRandom village in the salt marsh",
                "&YRandom village in the salt dunes",
                "&YRandom village in the desert canyons",
                "&YRandom village in the hills",
            };
            XRLCore.Core.Game.SetStringGameState("embark", list[State.StartingLocation]);

            __result = true;
            return false;
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
                if (!replaced && inst.Is(OpCodes.Callvirt, AccessTools.Method(typeof(ScreenBuffer), "Write", new Type[] { typeof(string), typeof(bool) }))) {
                    // take the output of this call to write
                    replaceNext = true;
                }
            }
        }

        static void AddToScreenBuffer(ScreenBuffer screenBuffer) {
            var _ = screenBuffer.Goto(5, 22)
                                .Write("Seed: " + XRLCore.Core.Game.GetStringGameState("OriginalWorldSeed"))
                                .Goto(15 + State.SeedLength, 22)
                                .Write("Memory: " + GC.GetTotalMemory(false))
                                .Goto(35 + State.SeedLength, 22)
                                .Write("Class: " + CreateCharacter.Template.Genotype + " " + CreateCharacter.Template.Subtype);
        }
    }
    [HarmonyPatch(typeof(QudHistoryFactory), "GenerateNewSultanHistory")]
    public static class PatchGenerateNewSultanHistory {
        static bool Prefix(ref HistoryKit.History __result) {
            if (!State.StubWorldbuilding) {
                return true;
            }
            __result = new HistoryKit.History(1);
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
