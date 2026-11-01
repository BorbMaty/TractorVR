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

    bool open, started;
    string controlsPage = "controllers";

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

    void Close()
    {
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
        board.Set("TRACTOR VR",
            "A John Deere 6195M on a farm near Reghin, in Transylvania.\n\n" +
            "TUTORIAL: nine short lessons, from driving off to ploughing, mowing and the trailer, each rated with up to five stars.\n" +
            "FREE ROAM: the farm, the fields and the roads are yours.\n" +
            "CONTROLS: what every button does.\n\n" +
            (DesktopMode.Active ? "Click a button." : "Touch a button with a fingertip or the tip of a controller."),
            new List<TouchBoard.Button>
            {
                B("TUTORIAL", ShowLessons), B(started ? "CONTINUE" : "FREE ROAM", Close),
                B("CONTROLS", () => ShowControls(controlsPage)), B("QUIT", Quit),
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
        list.Add(B("BACK", ShowMain));
        board.Set("TUTORIAL", "Pick a lesson: the tractor is taken to where it starts. The instructions are on a small board low on " +
                              "your left in the cab; a yellow glow shows the control to use or the place to drive to.", list);
    }

    void ShowControls(string page)
    {
        controlsPage = page;
        string title, body;
        switch (page)
        {
            case "hands":
                title = "CONTROLS: HAND TRACKING";
                body = "Put the controllers down: your hands appear.\n" +
                       "Grab (make a fist): the steering wheel rim, the speed lever knob, the remote levers, the indicator stalk, a door.\n" +
                       "Point with the index finger to press: panel buttons, console switches, the CommandCenter, these boards.\n" +
                       "Pointing never grabs.";
                break;
            case "cab":
                title = "CONTROLS: IN THE CAB";
                body = "Speed lever (right armrest): forward = drive, back = reverse; it stays where you leave it.\n" +
                       "Remote levers behind it: 1 rear hitch, 2 front linkage, 3 tipping trailer (pull back = raise).\n" +
                       "Right side console: light knob, work lights, beacon, hazards, 4WD, diff lock, PTO.\n" +
                       "Stalk left of the wheel: indicators (up = right, down = left).\n" +
                       "Right corner post: CommandCenter (fuel, temperature, power, hand throttle).\n" +
                       "Panel at the right front (MENU tab): every function as a button, and MAIN MENU.";
                break;
            case "keyboard":
                title = "CONTROLS: KEYBOARD AND MOUSE";
                body = "W / S: drive / brake and reverse.   A / D: steer.\n" +
                       "Q / E: rear hitch up / down (Ctrl: front linkage).   J: attach / detach.   P: PTO.\n" +
                       "X / Z: shift up / down.   T: auto / manual.   [ / ]: hand throttle.   I / K: tip.\n" +
                       "G: lights   N: work lights   O: beacon   Y: hazards   , / .: indicators   F: 4WD   L: diff lock\n" +
                       "C: view   U: sky   H: horn   M: menu   F9: try VR   F11: full screen\n" +
                       "Right mouse: look around.   Left mouse: press switches, drag the wheel and levers.";
                break;
            default:
                title = "CONTROLS: QUEST CONTROLLERS";
                body = "Left stick: steer (up / down: rear hitch).   Right stick: speed (forward = drive, back = brake, then reverse).\n" +
                       "Right stick flick right / left: shift up / down.   Right stick click: PTO.   Left stick click: attach / detach.\n" +
                       "A: cab / outside view.   B: recenter.   X: comfort vignette.   Y: auto / manual gearbox.\n" +
                       "Left trigger + left stick: front linkage.   Right trigger: horn.\n" +
                       "Grip: grab the wheel, the levers, the stalk, a door.   Menu button: the cab panel.";
                break;
        }
        board.Set(title, body, new List<TouchBoard.Button>
        {
            B("CONTROLLERS", () => ShowControls("controllers")), B("HANDS", () => ShowControls("hands")),
            B("IN THE CAB", () => ShowControls("cab")), B("KEYBOARD", () => ShowControls("keyboard")),
            B("BACK", ShowMain),
        });
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
