using System.ComponentModel;
using System.Text.Json.Serialization;

namespace OpenXRHandMenusEditor;

// ------------------------------------------------------------ dropdown helpers
public abstract class ChoiceConverter : StringConverter
{
    protected abstract IEnumerable<string> Values(ITypeDescriptorContext ctx);
    protected virtual bool Exclusive => false;
    public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => Exclusive;
    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new(Values(c).ToArray());
}
public static class Inputs
{
    public static readonly string[] All = BuildAll();
    static string[] BuildAll()
    {
        var l = new List<string>();
        foreach (var (h, b1, b2) in new[] { ("left", "x", "y"), ("right", "a", "b") })
        {
            l.Add($"{h}.{b1}"); l.Add($"{h}.{b1}.touch"); l.Add($"{h}.{b2}"); l.Add($"{h}.{b2}.touch");
            l.Add($"{h}.trigger"); l.Add($"{h}.trigger.touch"); l.Add($"{h}.grip");
            l.Add($"{h}.thumbstick"); l.Add($"{h}.thumbstick.touch");
            l.Add($"{h}.thumbstick.up"); l.Add($"{h}.thumbstick.down"); l.Add($"{h}.thumbstick.left"); l.Add($"{h}.thumbstick.right");
            l.Add($"{h}.thumbrest"); l.Add($"{h}.menu");
        }
        return l.ToArray();
    }
}
public class InputConv : ChoiceConverter { protected override IEnumerable<string> Values(ITypeDescriptorContext c) => Inputs.All; }
public class GestureConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "press", "tap", "hold", "double_tap", "while_held" }; }
public class BlockConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "none", "trigger", "all" }; }
public class BlockOpenConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "none", "minimal", "hand", "all" }; }
public class TypeConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "none", "key", "sequence", "xr_input", "wheel", "back", "close", "layer", "reload", "keyboard", "page", "adjust" }; }
public class ModeConv : ChoiceConverter { protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "tap", "hold", "toggle", "on", "off" }; }
public class ConfirmConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "auto", "release", "stick_return", "button" }; }
public class AnchorConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "hand", "hand_locked", "head", "head_locked" }; }
public class MethodConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "thumbstick", "point", "both" }; }
public class SelHandConv : ChoiceConverter { protected override bool Exclusive => true; protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "opener", "left", "right" }; }
public class CompConv : ChoiceConverter { protected override IEnumerable<string> Values(ITypeDescriptorContext c) => new[] { "trigger", "grip", "primary", "secondary", "thumbstick", "menu" }; }
public class WheelNameConv : ChoiceConverter { protected override IEnumerable<string> Values(ITypeDescriptorContext c) => MainForm.Current?.Profile?.Wheels.Keys.ToArray() ?? Array.Empty<string>(); }
public class LayerConv : ChoiceConverter
{
    protected override IEnumerable<string> Values(ITypeDescriptorContext c)
    {
        var p = MainForm.Current?.Profile;
        if (p == null) return Array.Empty<string>();
        var s = new SortedSet<string>();
        foreach (var b in p.Bindings)
        {
            if (!string.IsNullOrEmpty(b.Action?.Layer)) s.Add(b.Action.Layer);
            foreach (var l in (b.Layers ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) s.Add(l);
        }
        return s;
    }
}

// ------------------------------------------------------------ profile
public class ProfileModel
{
    [Category("1 General"), Description("Turn the whole overlay on or off for this game.")]
    [DisplayName("Overlay on for this game")]
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;

    [Category("1 General"), Description("What the game stops seeing while a wheel is open.\nnone: nothing\nminimal: stick, confirm and cancel buttons of the selecting hand\nhand: every input of the selecting and opening hand\nall: both controllers"), TypeConverter(typeof(BlockOpenConv))]
    [DisplayName("Hide game input while a wheel is open")]
    [JsonPropertyName("block_while_open")] public string BlockWhileOpen { get; set; } = "hand";

    [Category("1 General"), Description("Small vibrations when hovering and selecting.")]
    [DisplayName("Vibrations")]
    [JsonPropertyName("haptics")] public bool Haptics { get; set; } = true;

    [Category("1 General"), Description("Vibration strength, 0 to 1.")]
    [DisplayName("Vibration strength")]
    [JsonPropertyName("haptic_strength")] public float HapticStrength { get; set; } = 0.35f;

    [Category("1 General"), Description("Show small messages in the headset (layer on/off, config reloaded, config errors).")]
    [DisplayName("Messages in headset")]
    [JsonPropertyName("notifications")] public bool Notifications { get; set; } = true;

    [Category("1 General"), Description("Bring the game window to the front before sending keys. Turn on if keys go to the wrong window.")]
    [DisplayName("Bring game window to front before keys")]
    [JsonPropertyName("focus_game_window")] public bool FocusGameWindow { get; set; }

    [Category("2 Thresholds"), TypeConverter(typeof(ExpandableObjectConverter)), Description("When analog triggers, grips and stick directions count as pressed.")]
    [DisplayName("Button thresholds")]
    [JsonPropertyName("thresholds")] public Thresholds Thresholds { get; set; } = new();

    [Category("3 Look"), TypeConverter(typeof(ExpandableObjectConverter)), Description("Where the wheel appears and how it looks.")]
    [DisplayName("Wheel look and position")]
    [JsonPropertyName("style")] public StyleModel Style { get; set; } = new();

    [Category("4 Selecting"), TypeConverter(typeof(ExpandableObjectConverter)), Description("How items are picked on an open wheel.")]
    [DisplayName("Selecting items")]
    [JsonPropertyName("selection")] public SelectionModel Selection { get; set; } = new();

    [Category("5 Keyboard"), TypeConverter(typeof(ExpandableObjectConverter)), Description("The VR keyboard opened by a 'keyboard' action.")]
    [DisplayName("VR keyboard")]
    [JsonPropertyName("keyboard")] public KeyboardModel Keyboard { get; set; } = new();

    [Browsable(false)][JsonPropertyName("wrist_left")] public WristModel WristLeft { get; set; } = new();
    [Browsable(false)][JsonPropertyName("wrist_right")] public WristModel WristRight { get; set; } = new() { Mirror = true };
    // Older profiles had one panel; moved into WristLeft/WristRight on load.
    [Browsable(false)][JsonPropertyName("wrist_panel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WristModel LegacyWrist { get; set; }
    public WristModel WristFor(int h) => h == 0 ? WristLeft : WristRight;
    // The panel whose pages a forearm shows (either one can mirror the other, not both).
    public WristModel WristPages(int h) => WristFor(h).Mirror && !WristFor(1 - h).Mirror ? WristFor(1 - h) : WristFor(h);

    [Browsable(false)][JsonPropertyName("bindings")] public List<BindingModel> Bindings { get; set; } = new();
    [Browsable(false)][JsonPropertyName("wheels")] public Dictionary<string, WheelModel> Wheels { get; set; } = new();
}

public class Thresholds
{
    [DisplayName("Pressed at")]
    [Description("Trigger/grip value that counts as pressed.")][JsonPropertyName("press_on")] public float PressOn { get; set; } = 0.6f;
    [DisplayName("Released at")]
    [Description("Trigger/grip value that counts as released.")][JsonPropertyName("press_off")] public float PressOff { get; set; } = 0.4f;
    [DisplayName("Stick direction at")]
    [Description("Stick distance that counts as a direction press.")][JsonPropertyName("stick_on")] public float StickOn { get; set; } = 0.7f;
    [DisplayName("Stick released at")]
    [Description("Stick distance that counts as released.")][JsonPropertyName("stick_off")] public float StickOff { get; set; } = 0.5f;
    public override string ToString() => $"press {PressOn}/{PressOff}, stick {StickOn}/{StickOff}";
}

public class StyleModel
{
    [Description("hand: follows the opening hand\nhand_locked: appears at the hand and stays put\nhead: follows your view\nhead_locked: appears in front of you and stays put"), TypeConverter(typeof(AnchorConv))]
    [DisplayName("Wheel position")]
    [JsonPropertyName("anchor")] public string Anchor { get; set; } = "hand_locked";
    [DisplayName("Wheel size (radius, m)")]
    [Description("Wheel radius in metres.")][JsonPropertyName("radius_m")] public float Radius { get; set; } = 0.12f;
    [DisplayName("Distance in front of head (m)")]
    [Description("Head anchors: distance in front of you, metres.")][JsonPropertyName("head_distance_m")] public float HeadDistance { get; set; } = 0.45f;
    [DisplayName("Below eye level (m)")]
    [Description("Head anchors: how far below eye level, metres.")][JsonPropertyName("head_down_m")] public float HeadDown { get; set; } = 0.10f;
    [DisplayName("Above hand (m)")]
    [Description("Hand anchors: height above the hand, metres.")][JsonPropertyName("hand_up_m")] public float HandUp { get; set; } = 0.10f;
    [DisplayName("Toward head (m)")]
    [Description("Hand anchors: moved toward your head by this much, metres.")][JsonPropertyName("hand_toward_head_m")] public float HandToward { get; set; } = 0.06f;
    [DisplayName("Centre circle size")]
    [Description("Size of the centre circle, 0.2 to 0.6 of the radius.")][JsonPropertyName("inner_ratio")] public float InnerRatio { get; set; } = 0.34f;
    [DisplayName("Gap between segments (deg)")]
    [Description("Gap between segments, degrees.")][JsonPropertyName("gap_deg")] public float GapDeg { get; set; } = 1.5f;
    [Description("Font family name.")][JsonPropertyName("font")] public string Font { get; set; } = "Segoe UI";
    [DisplayName("Text size")]
    [Description("Text size multiplier.")][JsonPropertyName("font_scale")] public float FontScale { get; set; } = 1f;
    [DisplayName("Sharpness (pixels)")]
    [Description("Texture resolution in pixels (256 to 2048).")][JsonPropertyName("texture_px")] public int TexturePx { get; set; } = 768;
    [TypeConverter(typeof(ExpandableObjectConverter)), Description("Colours as #RRGGBB or #RRGGBBAA (AA = opacity).")]
    [DisplayName("Colours")]
    [JsonPropertyName("colors")] public ColorsModel Colors { get; set; } = new();
    public override string ToString() => $"{Anchor}, radius {Radius} m";
}

public class WristModel
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("mirror")] public bool Mirror { get; set; }
    [JsonPropertyName("hand"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string LegacyHand { get; set; }
    [JsonPropertyName("start_page")] public string Start { get; set; } = "main";
    [JsonPropertyName("pages")] public Dictionary<string, WheelModel> Pages { get; set; } = new();
    // Older profiles pointed the panel at a wheel; moved into Pages on load.
    [JsonPropertyName("wheel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string LegacyWheel { get; set; }
    [JsonPropertyName("face")] public string Face { get; set; } = "inner";
    [JsonPropertyName("size_m")] public float Size { get; set; } = 0.11f;
    [JsonPropertyName("toward_elbow_m")] public float TowardElbow { get; set; } = 0.13f;
    [JsonPropertyName("text_up")] public string TextUp { get; set; } = "fingers";
    [JsonPropertyName("lift_m")] public float Lift { get; set; } = 0.025f;
    [Browsable(false)][JsonPropertyName("side_m")] public float Side { get; set; }
    [JsonPropertyName("rotate_deg")] public float RotateDeg { get; set; }
    [JsonPropertyName("columns")] public int Columns { get; set; } = 3;
    [JsonPropertyName("facing_deg")] public float FacingDeg { get; set; } = 35f;
    [JsonPropertyName("gaze_deg")] public float GazeDeg { get; set; } = 20f;
    [JsonPropertyName("show_ms")] public int ShowMs { get; set; } = 250;
    [JsonPropertyName("hide_ms")] public int HideMs { get; set; } = 350;
    [JsonPropertyName("press")] public string Press { get; set; } = "both";
}

public class KeyboardModel
{
    [DisplayName("Width (m)")]
    [Description("Keyboard width in metres.")][JsonPropertyName("width_m")] public float Width { get; set; } = 0.56f;
    [DisplayName("Distance (m)")]
    [Description("Distance in front of you when it opens, metres.")][JsonPropertyName("distance_m")] public float Distance { get; set; } = 0.42f;
    [DisplayName("Below eye level (m)")]
    [Description("How far below eye level, metres.")][JsonPropertyName("below_eyes_m")] public float Below { get; set; } = 0.30f;
    [DisplayName("Close after Enter")]
    [Description("Close the keyboard after Enter (handy for chat boxes).")][JsonPropertyName("close_on_enter")] public bool CloseOnEnter { get; set; }
    public override string ToString() => $"{Width} m wide";
}

public class ColorsModel
{
    [JsonPropertyName("background")] public string Background { get; set; } = "#101418B0";
    [JsonPropertyName("segment")] public string Segment { get; set; } = "#283040D0";
    [JsonPropertyName("hover")] public string Hover { get; set; } = "#3C8CFFF0";
    [JsonPropertyName("text")] public string Text { get; set; } = "#FFFFFFFF";
    [JsonPropertyName("accent")] public string Accent { get; set; } = "#7FC4FFFF";
    [JsonPropertyName("border")] public string Border { get; set; } = "#FFFFFF60";
    [JsonPropertyName("toggled_on")] public string ToggledOn { get; set; } = "#2E9E5BE0";
    public override string ToString() => "colours";
}

public class SelectionModel
{
    [Description("thumbstick: push the stick toward an item\npoint: aim the controller at an item\nboth"), TypeConverter(typeof(MethodConv))]
    [DisplayName("Select items with")]
    [JsonPropertyName("method")] public string Method { get; set; } = "thumbstick";
    [Description("Which hand selects: the hand that opened the wheel, or always left/right."), TypeConverter(typeof(SelHandConv))]
    [DisplayName("Selecting hand")]
    [JsonPropertyName("hand")] public string Hand { get; set; } = "opener";
    [Description("How far the stick must be pushed before an item is hovered (0 to 1).")]
    [DisplayName("Stick deadzone")]
    [JsonPropertyName("deadzone")] public float Deadzone { get; set; } = 0.5f;
    [Description("Button on the selecting hand that confirms the hovered item (always works, and is the only way for 'button' confirm wheels)."), TypeConverter(typeof(CompConv))]
    [DisplayName("Confirm button")]
    [JsonPropertyName("confirm_button")] public string ConfirmButton { get; set; } = "trigger";
    [Description("Button on the selecting hand that goes back / closes. primary = A/X, secondary = B/Y."), TypeConverter(typeof(CompConv))]
    [DisplayName("Back / close button")]
    [JsonPropertyName("cancel_button")] public string CancelButton { get; set; } = "secondary";
    [Description("Release-confirm wheels: rest on a submenu item this long (ms) to open it. 0 = off.")]
    [DisplayName("Submenu open delay (ms)")]
    [JsonPropertyName("submenu_dwell_ms")] public int SubmenuDwellMs { get; set; } = 450;
    [Description("Close the wheel after picking an item.")]
    [DisplayName("Close after picking")]
    [JsonPropertyName("close_after_select")] public bool CloseAfterSelect { get; set; } = true;
    [Description("Close the wheel by itself after this many ms. 0 = never.")]
    [DisplayName("Auto close after (ms)")]
    [JsonPropertyName("auto_close_ms")] public int AutoCloseMs { get; set; }
    public override string ToString() => $"{Method}, {Hand} hand";
}

// ------------------------------------------------------------ bindings
public class BindingModel
{
    [Category("1 Binding"), Description("Your name for this binding.")]
    [JsonPropertyName("name")] public string Name { get; set; } = "New binding";
    [Category("1 Binding")][JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [Category("1 Binding"), TypeConverter(typeof(InputConv)), Description("The button. For a chord, join with + and put the button you press LAST at the end: left.grip+right.b\nInputs: left/right . x y a b trigger grip thumbstick thumbrest menu, optional .touch / .up .down .left .right (sticks).")]
    [JsonPropertyName("input")] public string Input { get; set; } = "right.thumbrest";
    [Category("1 Binding"), TypeConverter(typeof(GestureConv)), Description("press: fires when pressed\ntap: fires on a quick release (so a hold binding on the same button can coexist)\nhold: fires after hold_ms\ndouble_tap: two presses within double_tap_ms\nwhile_held: starts on press and stops on release (hold keys, momentary layers, hold-to-open wheels)")]
    [JsonPropertyName("gesture")] public string Gesture { get; set; } = "press";
    [Category("2 Timing")][JsonPropertyName("hold_ms")] public int HoldMs { get; set; } = 400;
    [Category("2 Timing")][JsonPropertyName("tap_ms")] public int TapMs { get; set; } = 300;
    [Category("2 Timing")][JsonPropertyName("double_tap_ms")] public int DoubleTapMs { get; set; } = 350;
    [Category("3 Conditions"), TypeConverter(typeof(LayerConv)), Description("Only active while ALL these layers are on (comma separated). This is how a chord or toggle changes what normal buttons do.")]
    [JsonPropertyName("layers")] public string Layers { get; set; } = "";
    [Category("3 Conditions"), TypeConverter(typeof(LayerConv)), Description("Not active while any of these layers is on (comma separated).")]
    [JsonPropertyName("not_layers")] public string NotLayers { get; set; } = "";
    [Category("4 Game input"), TypeConverter(typeof(BlockConv)), Description("Hide the button from the game while this binding is held.\nnone: game still sees it\ntrigger: the last button of the chord is hidden\nall: every button of the chord is hidden (after the chord completes)")]
    [JsonPropertyName("block")] public string Block { get; set; } = "trigger";
    [Category("4 Game input"), Description("Hold bindings: if you let go before hold_ms, send a short press of the button to the game, so a tap still does what the game normally does.")]
    [JsonPropertyName("tap_passthrough")] public bool TapPassthrough { get; set; }
    [Category("5 Action"), TypeConverter(typeof(ExpandableObjectConverter))]
    [JsonPropertyName("action")] public ActionModel Action { get; set; } = new();
}

public class ActionModel
{
    [Description("key: send a key/mouse combo\nsequence: macro with waits\nxr_input: fake a press of a controller button inside the game\nwheel: open a wheel (or a submenu, inside a wheel)\nback / close: wheel navigation\nlayer: switch a layer\nreload: reload this profile\nkeyboard: open/close the VR keyboard (point + trigger to type, B/Y closes)"), TypeConverter(typeof(TypeConv))]
    [JsonPropertyName("type")] public string Type { get; set; } = "key";
    [Description("key: combo such as Ctrl+S, F5, Esc, Space, LMB, WheelUp, Num5")]
    [JsonPropertyName("keys"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Keys { get; set; }
    [Description("key / xr_input: tap, hold (held while the binding is held) or toggle.\nlayer: hold, toggle, on, off"), TypeConverter(typeof(ModeConv))]
    [JsonPropertyName("mode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Mode { get; set; }
    [Description("How long a tap is held down, ms.")]
    [JsonPropertyName("tap_ms")] public int TapMs { get; set; } = 60;
    [Description("sequence: steps split by ;  e.g.  Ctrl+C; wait 100; Ctrl+V; down W; wait 1500; up W")]
    [JsonPropertyName("steps"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Steps { get; set; }
    [Description("wheel: which wheel to open"), TypeConverter(typeof(WheelNameConv))]
    [JsonPropertyName("wheel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Wheel { get; set; }
    [Description("wheel: how an item is picked.\nauto: thumbrest/touch openers use stick_return, hold/while_held openers use release, others use button\nrelease: let go of the opener\nstick_return: push stick to an item and let it spring back\nbutton: press the confirm button"), TypeConverter(typeof(ConfirmConv))]
    [JsonPropertyName("confirm"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Confirm { get; set; }
    [Description("layer: the layer name"), TypeConverter(typeof(LayerConv))]
    [JsonPropertyName("layer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Layer { get; set; }
    [Description("xr_input: the controller input the game should think is pressed, e.g. left.menu or right.thumbstick.up"), TypeConverter(typeof(InputConv))]
    [JsonPropertyName("target"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Target { get; set; }
    [Description("page: the wrist panel page to open"), JsonPropertyName("page"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Page { get; set; }

    public override string ToString() => Type switch
    {
        "key" => $"key {Keys} {Mode}".Trim(),
        "sequence" => $"macro: {Steps}",
        "xr_input" => $"game input {Target} {Mode}".Trim(),
        "wheel" => $"wheel {Wheel}",
        "page" => $"page {Page}",
        "layer" => $"layer {Layer} {Mode}".Trim(),
        _ => Type,
    };
}

// ------------------------------------------------------------ wheels
public class WheelModel
{
    [Description("Internal name, used by 'wheel' actions.")][JsonIgnore] public string Name { get; set; } = "wheel";
    [Description("Shown in the centre of the wheel.")][JsonPropertyName("title")] public string Title { get; set; } = "Wheel";
    [Browsable(false)][JsonPropertyName("items")] public List<WheelItemModel> Items { get; set; } = new();
}

public class WheelItemModel
{
    [Description("Untick to hide this item from the wheel without deleting it.")][JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [Description("Text on the segment.")][JsonPropertyName("label")] public string Label { get; set; } = "Item";
    [Description("Shown in the centre while hovered.")][JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Description { get; set; }
    [Description("PNG file: full path, or a file name inside the profiles\\icons folder.")][JsonPropertyName("icon"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Icon { get; set; }
    [Description("Segment colour #RRGGBB or #RRGGBBAA; empty = default.")][JsonPropertyName("color"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Color { get; set; }
    [TypeConverter(typeof(ExpandableObjectConverter))][JsonPropertyName("action")] public ActionModel Action { get; set; } = new();
}
