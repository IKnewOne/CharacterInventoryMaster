namespace CharacterInventoryMaster.Config;

public class ModConfig {
	public static string ConfigName = "CharacterInventoryMaster.json";
	public static ModConfig Instance { get; set; } = new ModConfig();

	public bool showYear = true;
	public bool showMonth  = true;
	public bool showDay  = true;
	public bool showTime  = true;
	public bool useTemperatureDescriptors  = false;
	public string temperatureBreakpoints = "-10, 0, 5, 15, 25, 30";
	public string temperatureDescriptors = "Biting cold, Very Cold, Cold, Freezing, Chilly, Comfortable, Warm, Sweltering";
}
