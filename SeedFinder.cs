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
    using XRL.Rules;
    using XRL.UI;
    using XRL.World;
    using XRL.World.Parts;

    [Serializable]
    public static class State {
        public const string Name = "Kizby"; // change this if you want
        public const int StartingLocation = 0; // {Joppa, marsh, dunes, canyon, hills}

        public static ulong _Seed = 0;//Decode("6NQ");
        public static string Seed {
            get {
                if (_Seed == 0 && File.Exists("seed.txt")) {
                    using (StreamReader reader = new StreamReader("seed.txt")) {
                        _ = ulong.TryParse(reader.ReadLine(), out _Seed);
                    }
                }
                return Encode(_Seed);
            }
        }

        public static bool InGenerateEquipment = false;
        public static int LastRoll = 999;
        public static int BestRoll = 999;
        public static bool WantNextRoll = false;

        public static string LastReseed = "";
        public static int RollsSinceReseed = 0;
        public static string StateAtMeasure = "";

        public static bool StubWorldbuilding = true;
        public static bool ForceCrayons = false;

        public static List<string> CrayonSeeds = new List<string>();
        public static SortedSet<int> Rolls = new SortedSet<int>();
        public static int RollCount = 0;

        public static string Encode(ulong num) {
            string result = "";
            do {
                result = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[(int)(num % 36ul)] + result;
                num /= 36;
            } while (num != 0);
            return result;
        }

        public static ulong Decode(string num) {
            ulong result = 0;
            foreach (var c in num) {
                result *= 36;
                if ('0' <= c && c <= '9') {
                    result += (ulong)(c - '0');
                } else {
                    result += (ulong)(c - 'A') + 10;
                }
            }
            return result;
        }

        public static bool Test() {
            var player = CreateCharacter.Template.PlayerBody;
            if (player.Inventory.HasObject("BoxOfCrayons")) {
                CrayonSeeds.Insert(0, Seed);
            }
            return player.Inventory.HasObject(o => o.GetLongProperty("Nanocrayons") == 1);
        }

        public static bool AfterNewGame() {
            // only actually run the game if we pass the test
            if (!Test()) {
                ++_Seed;
                if (_Seed % 36 == 0) {
                    using (StreamWriter writer = new StreamWriter("seed.txt")) {
                        writer.WriteLine(_Seed);
                    }
                }
                return true;
            }
            return false;
        }

        public static void Measure() {
            StateAtMeasure = LastReseed + " + " + RollsSinceReseed;
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
    [HarmonyPatch(typeof(Popup), "AskString")]
    public static class PatchAskString {
        static bool Prefix(ref string Message, ref string __result) {
            if (Message == "Enter a world seed.") {
                __result = State.Seed;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Popup), "Show")]
    public static class PatchShow {
        static bool Prefix(ref string Message) {
            if (Message == "You embark for the caves of Qud.") {
                // no popup for this
                return false;
            }
            return true;
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
                if (inst.Is(OpCodes.Call, AccessTools.Method(typeof(CreateCharacter), "GenerateEquipment"))) {
                    break;
                }
            }
            yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(State), "AfterNewGame"));
            // branch to "start" instead of returning at the end if AfterNewGame tells us to
            yield return new CodeInstruction(OpCodes.Brtrue, createCharacterLabel);
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
                                .Write("World Seed: " + XRLCore.Core.Game.GetStringGameState("OriginalWorldSeed"))
                                .Goto(25, 22)
                                .Write("Memory: " + GC.GetTotalMemory(false))
                                .Goto(45, 22)
                                .Write("Best roll: " + State.BestRoll)
                                .Goto(62, 22)
                                .Write("Last roll: " + State.LastRoll)
                                .Goto(25, 21)
                                .Write("Class: " + CreateCharacter.Template.Genotype + " " + CreateCharacter.Template.Subtype)
                                .Goto(5, 23)
                                .Write("Rolls (" + State.RollCount + "): " + State.Rolls.Join());
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
    [HarmonyPatch(typeof(Crayons), "HandleEvent", new Type[] { typeof(ObjectCreatedEvent) })]
    public static class PatchHandleEvent {
        static void Prefix() {
            State.WantNextRoll = State.InGenerateEquipment;
        }
    }
    [HarmonyPatch(typeof(Stat), "Random", new Type[] { typeof(int), typeof(int) })]
    public static class PatchRandom {
        static void Postfix(ref int __result) {
            if (State.WantNextRoll) {
                State.LastRoll = __result;
                State.Rolls.Add(__result);
                ++State.RollCount;
                if (__result < State.BestRoll) {
                    State.BestRoll = __result;
                }
                State.WantNextRoll = false;
            }
        }
    }
    [HarmonyPatch(typeof(Stat), "ReseedFrom", new Type[] { typeof(string), typeof(bool) })]
    public static class PatchReseedFrom {
        static void Prefix(ref string Seed) {
            State.LastReseed = Seed;
            State.RollsSinceReseed = 0;
        }
    }
    [HarmonyPatch(typeof(Random), "InternalSample")]
    public static class PatchInternalSample {
        static void Prefix(Random __instance) {
            if (__instance == Stat.Rnd) {
                ++State.RollsSinceReseed;
            }
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "GenerateEquipment")]
    public static class PatchGenerateEquipment {
        static void Prefix() {
            State.InGenerateEquipment = true;
            //State.Measure();
        }
        static void Postfix() {
            State.InGenerateEquipment = false;
        }
    }
    [HarmonyPatch(typeof(World.Encounters.PsychicManager), "Init")]
    public static class PatchWhereMeasuring {
        static void Postfix() {
            State.Measure();
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "AddItem", new Type[] { typeof(string) })]
    public static class PatchAddItem {
        static void Prefix(ref string Blueprint) {
            if (State.ForceCrayons) {
                Blueprint = "BoxOfCrayons";
            }
        }
    }
}
