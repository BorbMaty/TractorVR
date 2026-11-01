using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase 6: the start screen. At launch (and from the cab panel's MAIN MENU button) the player stands in the farmyard, 6 m from
/// the front-left corner of the tractor and looking at it, with a large <see cref="TouchBoard"/> at arm's length:
///   MAIN      TUTORIAL / FREE ROAM (CONTINUE once driving) / CONTROLS / QUIT
///   LESSONS   the nine lessons with their best star rating, BACK
///   CONTROLS  information pages: Quest controllers, hand tracking, in the cab, keyboard and mouse
/// While the menu is open the rig stands at <see cref="eye"/> (<see cref="VRCameraRig.InMenu"/>), the tractor's controls are off and
/// the cab's own boards are hidden. On the desktop the board is drawn in the middle of the screen.
/// </summary>
public class StartMenu : MonoBehaviour
{
    public static StartMenu Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    public VRCameraRig rig;
    public TractorController tractor;
    public TractorInput input;
    public CabPanel panel;
    public Tutorial tutorial;
    public TouchBoard board;
    public Transform eye;        // where the player stands while the menu is open (moved next to the tractor on Open)
    public Texture2D[] guides;   // annotated pictures for CONTROLS (tools/annotate_guide.py --game, tools/keyboard_guide.py)
    public float width = 0.62f, pictureWidth = 1.0f;

    bool open, started;
    string controlsPage = "controllers";

    Texture2D Guide(string name) => guides == null ? null : System.Array.Find(guides, g => g != null && g.name == name);

    void Awake() => Instance = this;

    void Start() => Open();

    /// <summary>Shows the menu: the player is taken out of the cab and stands beside the tractor.</summary>
    public void Open()
    {
        open = true;
        if (tutorial) tutorial.Stop();
        // Stand 6 m off the tractor's front-left corner, eyes 1.65 m up, looking at the cab.
        Transform t = tractor.transform;
        Vector3 at = t.position + t.rotation * new Vector3(-4.5f, 0f, 6f);
        at.y = TerrainGenerator.SurfaceHeight(at.x, at.z) + 1.65f;
        Vector3 look = t.position + t.rotation * new Vector3(0f, 2f, 1f) - at;
        look.y = 0f;
        eye.SetPositionAndRotation(at, Quaternion.LookRotation(look));
        board.transform.SetPositionAndRotation(at + eye.rotation * new Vector3(0f, -0.14f, 0.6f),
                                               eye.rotation * Quaternion.Euler(12f, 0f, 0f));   // tilted back a little, facing the eye
        rig.InMenu = true;
        rig.menuAnchor = eye;
        rig.Recenter();
        input.scripted = true;            // no driving from the menu (scripted with zero sticks)
        input.scriptedThrottle = 0f; input.scriptedSteer = 0f;
        if (panel) panel.SetHidden(true);
        ShowMain();
    }

    /// <summary>Free roam from a fresh start: implements back in the shed, the tractor in the yard.</summary>
    void FreeRoam()
    {
        if (tutorial) tutorial.FreshStart();
        Close();
    }

    void Close()
    {
        board.width = width;
        board.maxColumns = 2;
        open = false;
        started = true;
        board.Hide();
        rig.InMenu = false;
        rig.Recenter();
        input.scripted = false;
        if (panel) panel.SetHidden(DesktopMode.Active);   // the desktop shows the panel as its screen menu instead
    }

    static TouchBoard.Button B(string label, System.Action press, int stars = -1) => new TouchBoard.Button { label = label, press = press, stars = stars };

    public void ShowMain()
    {
        if (!open) { Open(); return; }
        board.width = width;
        board.maxColumns = 2;
        board.Set("TRACTOR VR",
            "A John Deere 6195M on a farm near Reghin, in Transylvania.\n\n" +
            "TUTORIAL: nine short lessons, from driving off to ploughing, mowing and the trailer, each rated with up to five stars.\n" +
            "FREE ROAM: the farm, the fields and the roads are yours (a fresh start in the yard" + (started ? "; CONTINUE goes back to where you were" : "") + ").\n" +
            "CONTROLS: what every button does.\n\n" +
            (DesktopMode.Active ? "Click a button." : "Touch a button with a fingertip or the tip of a controller."),
            started ? new List<TouchBoard.Button>
            {
                B("CONTINUE", Close), B("FREE ROAM", FreeRoam), B("TUTORIAL", ShowLessons), B("CONTROLS", () => ShowControls(controlsPage)), B("QUIT", Quit),
            } : new List<TouchBoard.Button>
            {
                B("FREE ROAM", FreeRoam), B("TUTORIAL", ShowLessons), B("CONTROLS", () => ShowControls(controlsPage)), B("QUIT", Quit),
            });
    }

    public void ShowLessons()
    {
        if (!open) Open();
        var list = new List<TouchBoard.Button>();
        for (int i = 0; i < tutorial.LessonCount; i++)
        {
            int k = i;
            list.Add(B($"{i + 1}. {tutorial.LessonName(i)}", () => { Close(); tutorial.StartLesson(k); }, Tutorial.BestStars(i)));
        }
        list.Add(B("FREE ROAM", FreeRoam));
        list.Add(B("BACK", ShowMain));
        board.width = width;
        board.maxColumns = 2;
        board.Set("TUTORIAL", "Pick a lesson: the tractor is taken to where it starts. The instructions are on a small board low on " +
                              "your left in the cab; a yellow glow shows the control to use or the place to drive to.", list);
    }

    static readonly (string id, string button, string title, string guide, string text)[] ControlPages =
    {
        ("controllers", "CONTROLLERS", "CONTROLS: QUEST CONTROLLERS", "guide_controllers",
            "Put the controllers down to use your hands: make a fist on the wheel, a lever or a door to grab it; point with the index finger to press."),
        ("driver", "IN THE CAB", "CONTROLS: IN THE CAB", "guide_driver",
            "Grab with the grip button (or a fist), press with the controller tip (or a fingertip)."),
        ("levers", "LEVERS", "CONTROLS: SPEED LEVER AND REMOTE LEVERS", "guide_console",
            "The levers stay where you leave them; the remote levers spring back to the middle."),
        ("switches", "SWITCHES", "CONTROLS: THE SWITCH PANEL", "guide_switches",
            "Touch a switch to flip it; the small lamp in it shows when it is on."),
        ("panel", "PANEL", "CONTROLS: THE CAB PANEL", "guide_panel",
            "Every function as a button: open it with the MENU tab or the left controller's menu button. MAIN MENU brings you back here."),
        ("keyboard", "KEYBOARD", "CONTROLS: KEYBOARD AND MOUSE", "guide_keyboard",
            "The PC version plays on the monitor when no headset is in use (F9: try VR)."),
    };

    void ShowControls(string page)
    {
        int k = System.Array.FindIndex(ControlPages, c => c.id == page);
        if (k < 0) k = 0;
        var c = ControlPages[k];
        controlsPage = c.id;
        var list = new List<TouchBoard.Button>();
        foreach (var other in ControlPages)
        {
            var id = other.id;
            list.Add(B(other.id == c.id ? "> " + other.button : other.button, () => ShowControls(id)));
        }
        list.Add(B("BACK", ShowMain));
        board.width = pictureWidth;
        board.maxColumns = 4;
        board.Set(c.title, c.text, list, -1, Guide(c.guide));
    }

    void Quit()
    {
#if UNITY_EDITOR
        Debug.Log("[StartMenu] QUIT (ignored in the editor)");
#else
        Application.Quit();
#endif
    }

    /// <summary>Editor renders: the menu page without moving the rig.</summary>
    public void Preview(string page)
    {
        Instance = this; open = true;
        if (page == "lessons") ShowLessons();
        else if (page.StartsWith("controls")) ShowControls(page.Substring(page.IndexOf(':') + 1));
        else ShowMain();
        board.Refresh(true);
    }
}
