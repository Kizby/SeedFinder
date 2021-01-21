namespace XRL.SeedFinder {
    using System;
    using System.Collections.Generic;
    using System.Reflection.Emit;
    using ConsoleLib.Console;
    using HarmonyLib;
    using XRL.Core;
    using XRL.UI;

    public static class State {
        public const string Name = "Kizby"; // change this if you want
        public const int StartingLocation = 0; // {Joppa, marsh, dunes, canyon, hills}

        public static ulong _Seed = Decode("61J");
        public static string Seed => Encode(_Seed);

        public static bool Running = false;

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
            var player = XRLCore.Core.Game.Player.Body;
            return player.Inventory.HasObject(o => o.GetLongProperty("Nanocrayons") == 1);
        }

        public static bool AfterNewGame() {
            return Running;
        }
    }
    [HarmonyPatch(typeof(CreateCharacter), "PickGameType")]
    public static class PatchPickGameType {
        static bool Prefix(ref string __result) {
            // if we get here, we're running
            State.Running = true;

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
    [HarmonyPatch(typeof(XRLCore), "RunGame")]
    public static class PatchRunGame {
        static bool Prefix() {
            // only actually run the game if we pass the test
            if (!State.Test()) {
                ++State._Seed;
                return false;
            }
            State.Running = false;
            return true;
        }
    }
    [HarmonyPatch(typeof(XRLCore), "NewGame")]
    public static class PatchNewGame {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
            var start = generator.DefineLabel();
            bool first = true;
            foreach (var inst in instructions) {
                var actual = inst;
                if (first) {
                    // add a "start" label to the first instruction
                    actual = new CodeInstruction(inst);
                    actual.labels.Add(start);
                    first = false;
                }
                yield return actual;
                if (inst.Is(OpCodes.Callvirt, AccessTools.Method(typeof(GameManager), "PopGameView"))) {
                    break;
                }
            }
            yield return new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(State), "AfterNewGame"));
            // branch to "start" instead of returning at the end if AfterNewGame tells us to
            yield return new CodeInstruction(OpCodes.Brtrue, start);
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
                                .Goto(30, 22)
                                .Write("Memory: " + GC.GetTotalMemory(false));
        }
    }
}
