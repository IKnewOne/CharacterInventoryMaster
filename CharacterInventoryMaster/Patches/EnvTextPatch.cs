using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using CharacterInventoryMaster.Config;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace CharacterInventoryMaster;

[HarmonyPatch(typeof(CharacterExtraDialogs), "getEnvText")]
public class EnvTextPatch {
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var matcher = new CodeMatcher(instructions);

        // Find PrettyDate() call and inject our string manipulation
        // We're looking for: callvirt IWorldCalendar.PrettyDate()
        // Then we want to insert a call to our helper method
        matcher.Start()
            .MatchStartForward(
                new CodeMatch(i => i.opcode == OpCodes.Callvirt &&
                                   i.operand is MethodInfo mi &&
                                   mi.Name == "PrettyDate")
            );

        if (matcher.IsValid) {
            // After the PrettyDate() call, insert our helper method call
            matcher.Advance(1)
                .Insert(
                    new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(EnvTextPatch), nameof(HandleDateTime)))
                );
        }

        // Find Lang.Get("character-envtext", ...) call and inject temperature replacement
        // Looking at the IL: the args are built as an object array
        // We need to find where str2 (ldloc.2) is loaded into the array at index 1
        // Sequence: ldstr "character-envtext", newarr, dup, ldc.i4.0, ldloc.0, stelem.ref,
        //           dup, ldc.i4.1, ldloc.2 (str2), stelem.ref, ...
        matcher.Start()
            .MatchStartForward(
                new CodeMatch(i => i.opcode == OpCodes.Ldstr &&
                                   i.operand is string s &&
                                   s == "character-envtext")
            );

        if (matcher.IsValid) {
            // Find the ldloc.2 (str2) that comes after ldc.i4.1 (array index 1)
            // This ensures we get the right str2 load
            matcher.MatchStartForward(
                new CodeMatch(OpCodes.Ldc_I4_1),  // Loading index 1 for array
                new CodeMatch(OpCodes.Ldloc_2)     // Loading str2
            );

            if (matcher.IsValid) {
                // We're at ldc.i4.1, advance to ldloc.2, then inject after it
                matcher.Advance(2)
                    .Insert(
                        new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(EnvTextPatch), nameof(HandleTemperature)))
                    );
            }
        }



        return matcher.InstructionEnumeration();
    }

    private static string HandleDateTime(string dateString) {
        if (string.IsNullOrEmpty(dateString)) return dateString;
        var config = ModConfig.Instance;

        // Format: "4. June, Year 0, 06:43"
        // Structure: [day][. ][month][, ][year][, ][time]

        // Split by commas first to separate the main parts
        var parts = dateString.Split(',');

        if (parts.Length < 3) return dateString;

        var dayMonthPart = parts[0].Trim(); // "4. June"
        var dmParts = dayMonthPart.Split('.'); // ["4"," June"]

        var year = parts[1].Trim(); // "Year 0"
        var time = parts[2].Trim(); // "06:43"
        var day =  dmParts[0].Trim(); // "4"
        var month =  dmParts[1].Trim(); // "June"


        var sb = new StringBuilder();

        bool showNothing = !config.showYear &&!config.showMonth && !config.showDay && !config.showTime;

        if (showNothing) return CharacterInventoryMaster.LGet("no-date-display");

        if (config.showDay) {
            sb.Append($"{day}. ");
        }

        if (config.showMonth) {
            sb.Append($"{month}, ");
        }

        if (config.showYear) {
            sb.Append($"{year}, ");
        }

        if (config.showTime) {
            sb.Append($"{time}");
        }

        return sb.ToString().TrimEnd(' ', '.', ',');
    }

    private static string HandleTemperature(string temperatureString) {
        if (string.IsNullOrEmpty(temperatureString)) return temperatureString;

        var config = ModConfig.Instance;

        if (!config.useTemperatureDescriptors) return temperatureString;

        // Extract temperature value from string (e.g., "15°C" or "59°F")
        // Remove °C or °F and parse the number
        var tempValue = temperatureString.Replace("°C", "").Replace("°F", "").Trim();

        if (!int.TryParse(tempValue, out int temperature)) {
            return temperatureString; // If we can't parse, return original
        }

        // Parse and normalize breakpoints and descriptors
        var breakpoints = ParseTemperatureBreakpoints(config.temperatureBreakpoints);
        var descriptors = ParseTemperatureDescriptors(config.temperatureDescriptors, breakpoints.Count);

        if (descriptors.Count == 0 || breakpoints.Count == 0) {
            return temperatureString; // If no valid config, return original
        }

        // Find the appropriate descriptor based on temperature and breakpoints
        // With N breakpoints, we have N+1 ranges:
        // Range 0: temp < breakpoint[0]
        // Range 1: breakpoint[0] <= temp < breakpoint[1]
        // ...
        // Range N: temp >= breakpoint[N-1]

        int rangeIndex = 0;
        for (int i = 0; i < breakpoints.Count; i++) {
            if (temperature < breakpoints[i]) {
                break;
            }
            rangeIndex = i + 1;
        }

        return descriptors[rangeIndex];
    }

    private static List<int> ParseTemperatureBreakpoints(string input) {
        if (string.IsNullOrWhiteSpace(input)) {
            return new List<int>();
        }

        // Parse, trim, remove invalid/duplicates, and sort
        var breakpoints = input
            .Split(',')
            .Select(bp => bp.Trim())
            .Where(bp => !string.IsNullOrWhiteSpace(bp) && int.TryParse(bp, out _))
            .Select(bp => int.Parse(bp))
            .Distinct()
            .OrderBy(bp => bp)
            .ToList();

        return breakpoints;
    }

    private static List<string> ParseTemperatureDescriptors(string input, int breakpointCount) {
        if (string.IsNullOrWhiteSpace(input)) {
            return new List<string>();
        }

        // Parse, trim, remove empty
        var descriptors = input
            .Split(',')
            .Select(d => d.Trim())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .ToList();

        if (descriptors.Count == 0) {
            return descriptors;
        }

        // N breakpoints create N+1 ranges, so we need N+1 descriptors
        int requiredCount = breakpointCount + 1;

        // If we have fewer descriptors than needed, repeat the last one
        while (descriptors.Count < requiredCount) {
            descriptors.Add(descriptors[^1]);
        }

        return descriptors;
    }
}
