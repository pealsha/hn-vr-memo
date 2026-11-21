using System.Text.Json;
using System.Text.Json.Serialization;
using HandwriteMemo.Core;

namespace HandwriteMemo.App;

[JsonConverter(typeof(ToggleGestureModeConverter))]
internal enum ToggleGestureMode
{
    Disabled,
    WristButton
}

internal sealed class ToggleGestureModeConverter : JsonConverter<ToggleGestureMode>
{
    public override ToggleGestureMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() switch
            {
                "Disabled" => ToggleGestureMode.Disabled,
                "WristButton" or "WristDoubleTap" or "HeadDoubleTap" or "BackOfHand" => ToggleGestureMode.WristButton,
                _ => throw InvalidMode()
            },
            JsonTokenType.Number when reader.TryGetInt32(out int value) => value switch
            {
                2 => ToggleGestureMode.Disabled,
                0 or 1 or 3 => ToggleGestureMode.WristButton,
                _ => throw InvalidMode()
            },
            _ => throw InvalidMode()
        };

    public override void Write(Utf8JsonWriter writer, ToggleGestureMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ToggleGestureMode.Disabled => "Disabled",
            ToggleGestureMode.WristButton => "WristButton",
            _ => throw InvalidMode()
        });

    private static JsonException InvalidMode() => new("GestureMode must be WristButton or Disabled.");
}

internal sealed record AppOptions
{
    public const float MinimumBoardBrightness = .6f;
    public const float MediumBoardBrightness = .8f;
    public const float MaximumBoardBrightness = 1f;
    public const float DefaultBoardBrightness = MaximumBoardBrightness;
    public static IReadOnlyList<float> BoardBrightnessChoices { get; } = Array.AsReadOnly(new[]
        { MaximumBoardBrightness, MediumBoardBrightness, MinimumBoardBrightness });
    [JsonConverter(typeof(JsonStringEnumConverter<HandSide>))]
    public HandSide Handedness { get; init; } = HandSide.Right;
    public float GrabRadiusMeters { get; init; } = HandwriteMemo.Core.GrabSettings.DefaultGrabRadiusMetres;
    public OffsetMeters LeftPalmOffset { get; init; } = new();
    public OffsetMeters RightPalmOffset { get; init; } = new();
    public OffsetMeters RightIndexTipOffset { get; init; } = new(0, -.07f, -.035f);
    public float DrawingDistanceMeters { get; init; } = .15f;
    public float BoardDistanceMeters { get; init; } = .45f;
    public float BoardDownMeters { get; init; } = .12f;
    // Multiplies VR overlay RGB only; paper/ink data and PNG export are unchanged.
    public float BoardBrightness { get; init; } = DefaultBoardBrightness;
    public ToggleGestureMode GestureMode { get; init; } = ToggleGestureMode.WristButton;
    [JsonConverter(typeof(JsonStringEnumConverter<WristButtonActivationMode>))]
    public WristButtonActivationMode WristButtonActivationMode { get; init; } = WristButtonActivationMode.DoubleTap;
    public bool WristTapHapticsEnabled { get; init; }
    public OffsetMeters? WristTapCentreRightHanded { get; init; }
    public OffsetMeters? WristTapCentreLeftHanded { get; init; }
    [JsonIgnore]
    public System.Numerics.Vector3 CurrentWristTapCentre =>
        (Handedness == HandSide.Right ? WristTapCentreRightHanded : WristTapCentreLeftHanded)?.Vector ?? WristTapGesture.ContactCentre;
    public int TrashRetentionDays { get; init; } = 30;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutoStartRequest? PendingAutoStart { get; init; }
    public static IReadOnlyList<int> TrashRetentionChoices { get; } = Array.AsReadOnly(new[] { 0, 1, 14, 30, 60 });
    public string SaveFolder { get; init; } = AppDataPaths.MemoFolder(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    [JsonIgnore] public GrabSettings GrabSettings => new(GrabRadiusMeters);
    public static string DefaultConfigPath => AppDataPaths.ConfigPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static AppOptions Load(string? path) => Load(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static AppOptions Load(string? path, string localAppData)
    {
        string defaultConfig = AppDataPaths.ConfigPath(localAppData);
        path ??= File.Exists(defaultConfig) ? defaultConfig : null;
        var options = path is null ? new AppOptions() :
            JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path))
            ?? throw new ArgumentException("Configuration must be a JSON object.");
        if (!Enum.IsDefined(options.WristButtonActivationMode))
            throw new ArgumentException("WristButtonActivationMode: DoubleTap or Touch.");
        foreach (var offset in new[] { options.LeftPalmOffset, options.RightPalmOffset, options.RightIndexTipOffset })
            (offset ?? throw new ArgumentException("Hand offsets must be objects.")).Validate();
        ValidateWristTapCentre(options.WristTapCentreRightHanded);
        ValidateWristTapCentre(options.WristTapCentreLeftHanded);
        if (!(options.GrabRadiusMeters is >= .01f and <= .3f))
            throw new ArgumentException("GrabRadiusMeters: 0.01..0.3.");
        if (!float.IsFinite(options.DrawingDistanceMeters) || options.DrawingDistanceMeters is < .015f or > .5f)
            throw new ArgumentException("DrawingDistanceMeters: 0.015..0.5.");
        if (!float.IsFinite(options.BoardDistanceMeters) || options.BoardDistanceMeters is < .2f or > 1.2f ||
            !float.IsFinite(options.BoardDownMeters) || options.BoardDownMeters is < -.3f or > .5f)
            throw new ArgumentException("BoardDistanceMeters: 0.2..1.2; BoardDownMeters: -0.3..0.5.");
        if (!TrashRetentionChoices.Contains(options.TrashRetentionDays))
            throw new ArgumentException("TrashRetentionDays: 0, 1, 14, 30, or 60.");
        if (!Enum.IsDefined(options.Handedness))
            throw new ArgumentException("Handedness: Right or Left.");
        if (!float.IsFinite(options.BoardBrightness) || options.BoardBrightness is < MinimumBoardBrightness or > MaximumBoardBrightness)
            throw new ArgumentException("BoardBrightness: 0.6..1.0 (VR display only).");
        if (string.IsNullOrWhiteSpace(options.SaveFolder) || !Path.IsPathFullyQualified(options.SaveFolder))
            throw new ArgumentException("SaveFolder must be an absolute directory path.");
        return options with
        {
            SaveFolder = Path.GetFullPath(options.SaveFolder)
        };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    internal static void ValidateWristTapCentre(OffsetMeters? centre)
    {
        if (centre is not null && (!float.IsFinite(centre.X) || !float.IsFinite(centre.Y) || !float.IsFinite(centre.Z) ||
            !float.IsFinite(centre.Vector.LengthSquared())))
            throw new ArgumentException("Wrist tap centres must be finite grip-local coordinates.");
    }
}
