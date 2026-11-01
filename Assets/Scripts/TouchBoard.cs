using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A touch board in the world (phase 6): a title, a block of text, an optional row of rating stars and a grid of buttons, pressed
/// with a fingertip or the controller tip (like the cab panel). Built at runtime from quads + TextMeshes; the text is measured with
/// the font's glyph widths, so it wraps inside the board and the board grows to fit; long button labels go onto two lines or shrink.
/// Used by the start menu (a big board in the yard) and the tutorial (a small one in the cab). On the desktop the same content is
/// drawn on screen by <see cref="DesktopMode"/> (<see cref="DrawGUI"/>): <see cref="screenCentered"/> boards in the middle, the
/// others top left.
/// </summary>
public class TouchBoard : MonoBehaviour
{
    public struct Button { public string label; public System.Action press; public int stars; }   // stars -1 = none

    public static readonly List<TouchBoard> All = new List<TouchBoard>();

    public VRHands hands;
    public Material buttonMat, buttonHotMat, panelMat, textMat, starOnMat, starOffMat, pictureMat;
    public AudioSource click;
    public float width = 0.36f, titleSize = 0.0135f, bodySize = 0.0105f, buttonSize = 0.0105f, buttonHeight = 0.036f;
    public int maxColumns = 2;
    public bool screenCentered;

    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public int Stars { get; private set; } = -1;
    /// <summary>Optional picture between the title and the text (the CONTROLS pages: annotated cab / controller / keyboard).</summary>
    public Texture Picture { get; private set; }
    public bool Visible { get; private set; }
    readonly List<Button> buttons = new List<Button>();
    public int ButtonCount => buttons.Count;
    public Button ButtonAt(int i) => buttons[i];

    const float Margin = 0.012f, Gap = 0.006f;

    Transform board, buttonRoot, starRow;
    TextMesh titleText, bodyText;
    MeshRenderer back, picture;
    MaterialPropertyBlock pictureBlock;
    Font font;
    readonly List<(Transform t, MeshRenderer face, TextMesh label, bool inside)> made = new List<(Transform, MeshRenderer, TextMesh, bool)>();
    bool dirty = true;
    float cooldown;
    string lastBody;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    public void Set(string title, string body, IEnumerable<Button> list, int stars = -1, Texture picture = null)
    {
        Title = title; Body = body; Stars = stars; Picture = picture;
        buttons.Clear();
        buttons.AddRange(list);
        Visible = true;
        dirty = true;
    }

    /// <summary>Changes only the text (e.g. a step's live status), keeping the buttons.</summary>
    public void SetBody(string body) { if (body != Body) { Body = body; } }

    public void Hide() { Visible = false; dirty = true; }

    public void Press(int i)
    {
        if (i < 0 || i >= buttons.Count) return;
        if (click) click.Play();
        buttons[i].press?.Invoke();
    }

    // ================================================================== world board
    void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        board = new GameObject("Board").transform;
        board.SetParent(transform, false);
        back = Quad("Back", board, panelMat);
        titleText = Text("Title", board, titleSize, new Color(1f, 0.85f, 0.3f));
        bodyText = Text("Body", board, bodySize, new Color(0.95f, 0.95f, 0.9f));
        buttonRoot = new GameObject("Buttons").transform;
        buttonRoot.SetParent(board, false);
        starRow = new GameObject("Stars").transform;
        starRow.SetParent(board, false);
        picture = Quad("Picture", board, pictureMat);
        pictureBlock = new MaterialPropertyBlock();
    }

    /// <summary>Updates the board (also callable outside play mode for editor renders). Returns false while hidden.</summary>
    public bool Refresh(bool show)
    {
        if (board == null) Build();
        show &= Visible;
        board.gameObject.SetActive(show);
        if (!show) return false;
        string body = WrapToWidth(bodyText, Body, width - 2f * Margin);
        if (dirty || body != lastBody)
        {
            int oldLines = lastBody == null ? -1 : lastBody.Split('\n').Length;
            lastBody = body;
            titleText.text = Title;
            FitSize(titleText, Title, titleSize, width - 2f * Margin);
            bodyText.text = body;
            if (dirty || body.Split('\n').Length != oldLines) { dirty = false; Layout(body.Split('\n').Length); }
        }
        return true;
    }

    void Update()
    {
        if (!Refresh(!DesktopMode.Active)) return;
        cooldown -= Time.deltaTime;
        for (int i = 0; i < made.Count; i++)
        {
            var b = made[i];
            bool inside = false;
            if (hands != null)
                foreach (var h in hands.Both)
                {
                    if (!h.Tracked) continue;
                    Vector3 q = b.t.InverseTransformPoint(h.PokeTip);   // button units (scaled), z 1 = 3 cm
                    if (Mathf.Abs(q.x) < 0.52f && Mathf.Abs(q.y) < 0.6f && q.z > -0.4f && q.z < 1.2f) inside = true;
                }
            b.face.sharedMaterial = inside ? buttonHotMat : buttonMat;
            if (inside && !b.inside && cooldown <= 0f) { cooldown = 0.4f; made[i] = (b.t, b.face, b.label, true); Press(i); return; }
            made[i] = (b.t, b.face, b.label, inside);
        }
    }

    void Layout(int lines)
    {
        foreach (var b in made) Kill(b.t.gameObject);
        made.Clear();
        for (int i = starRow.childCount - 1; i >= 0; i--) Kill(starRow.GetChild(i).gameObject);
        int n = buttons.Count;
        int cols = n <= 4 ? Mathf.Clamp(n, 1, maxColumns) : Mathf.Min(maxColumns, (n + 1) / 2);
        int rows = (n + cols - 1) / cols;
        float titleH = LineHeight(titleText) + 0.006f;
        float starH = Stars >= 0 ? titleSize * 2.6f : 0f;
        float picW = width - 2f * Margin;
        float picH = Picture != null && pictureMat != null ? picW * Picture.height / Picture.width + 0.008f : 0f;
        float textH = titleH + starH + picH + lines * LineHeight(bodyText) + 0.004f;
        float h = Gap + textH + Gap + rows * (buttonHeight + Gap) + Gap;
        back.transform.localScale = new Vector3(width, h, 1f);
        back.transform.localPosition = new Vector3(0f, 0f, 0.003f);
        float top = h * 0.5f - Gap;
        titleText.transform.localPosition = new Vector3(-width * 0.5f + Margin, top, -0.001f);
        if (Stars >= 0) MakeStars(starRow, new Vector3(-width * 0.5f + Margin + titleSize, top - titleH - starH * 0.45f, -0.001f), titleSize * 1.1f, Stars);
        picture.gameObject.SetActive(picH > 0f);
        if (picH > 0f)
        {
            picture.transform.localScale = new Vector3(picW, picH - 0.008f, 1f);
            picture.transform.localPosition = new Vector3(0f, top - titleH - starH - (picH - 0.008f) * 0.5f, -0.001f);
            pictureBlock.SetTexture("_BaseMap", Picture);
            picture.SetPropertyBlock(pictureBlock);
        }
        bodyText.transform.localPosition = new Vector3(-width * 0.5f + Margin, top - titleH - starH - picH, -0.001f);
        float bw = (width - (cols + 1) * Gap) / cols;
        float y0 = top - textH - Gap - buttonHeight * 0.5f;
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            var t = new GameObject("Button_" + i).transform;
            t.SetParent(buttonRoot, false);
            t.localPosition = new Vector3(-width * 0.5f + Gap + bw * 0.5f + c * (bw + Gap), y0 - r * (buttonHeight + Gap), 0f);
            t.localScale = new Vector3(bw, buttonHeight, 0.03f);
            var face = Quad("Face", t, buttonMat);
            var label = Text("Label", t, buttonSize, new Color(0.95f, 0.95f, 0.9f));
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.transform.localScale = new Vector3(1f / bw, 1f / buttonHeight, 1f);
            float labelW = bw - 0.012f;
            if (buttons[i].stars >= 0)
            {
                // Stars at the right end of the button (5 stars of radius r, 2.1 r apart: 10.4 r wide), the label left of them.
                float sr = buttonSize * 0.42f, rowW = sr * 10.4f;
                float right = bw * 0.5f - 0.008f, rowLeft = right - rowW;
                var row = new GameObject("Stars").transform;
                row.SetParent(t, false);
                row.localScale = new Vector3(1f / bw, 1f / buttonHeight, 1f);
                row.localPosition = new Vector3((rowLeft + sr) / bw, 0f, -0.07f);
                MakeStars(row, Vector3.zero, sr, buttons[i].stars);
                float left = -bw * 0.5f + 0.006f, labelRight = rowLeft - 0.006f;
                labelW = labelRight - left;
                label.transform.localPosition = new Vector3((left + labelRight) * 0.5f / bw, 0f, -0.07f);
            }
            else label.transform.localPosition = new Vector3(0f, 0f, -0.07f);
            FitButton(label, buttons[i].label, labelW);
            made.Add((t, face, label, true));   // "inside" until the hand leaves: no press on the frame a page opens
        }
    }

    void MakeStars(Transform parent, Vector3 at, float r, int count)
    {
        for (int k = 0; k < 5; k++)
        {
            var go = new GameObject("Star");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at + new Vector3(k * r * 2.1f, 0f, 0f);
            go.transform.localScale = Vector3.one * r;
            go.AddComponent<MeshFilter>().sharedMesh = StarMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = k < count ? starOnMat : starOffMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    static Mesh starMesh;
    /// <summary>Five-pointed star, radius 1, facing -Z (towards the reader of the board).</summary>
    static Mesh StarMesh
    {
        get
        {
            if (starMesh != null) return starMesh;
            var v = new Vector3[11];
            v[0] = Vector3.zero;
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f, r = i % 2 == 0 ? 1f : 0.42f;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            }
            var t = new int[30];
            for (int i = 0; i < 10; i++) { t[i * 3] = 0; t[i * 3 + 1] = 1 + (i + 1) % 10; t[i * 3 + 2] = 1 + i; }
            starMesh = new Mesh { name = "Star", vertices = v, triangles = t };
            starMesh.RecalculateNormals();
            starMesh.RecalculateBounds();
            return starMesh;
        }
    }

    static void Kill(GameObject g) { if (Application.isPlaying) Destroy(g); else DestroyImmediate(g); }

    TextMesh Text(string name, Transform parent, float height, Color c)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 40;
        tm.characterSize = CharSize(height, tm);
        tm.anchor = TextAnchor.UpperLeft;
        tm.alignment = TextAlignment.Left;
        tm.color = c;
        go.GetComponent<MeshRenderer>().sharedMaterial = textMat;
        return tm;
    }

    static MeshRenderer Quad(string name, Transform parent, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    // ---- text measuring (TextMesh: one font pixel = characterSize * 0.1 local units)
    float Width(TextMesh tm, string line)
    {
        font.RequestCharactersInTexture(line, tm.fontSize, tm.fontStyle);
        float w = 0f;
        foreach (char c in line)
            if (font.GetCharacterInfo(c, out CharacterInfo ci, tm.fontSize, tm.fontStyle)) w += ci.advance;
        return w * tm.characterSize * 0.1f;
    }

    // Line pitch from the TextMesh size (font.lineHeight is at the font's own size); 1.3 x the size matches the render.
    static float LineHeight(TextMesh tm) => tm.fontSize * tm.characterSize * 0.1f * 1.3f * tm.lineSpacing;
    static float CharSize(float height, TextMesh tm) => height / (tm.fontSize * 0.1f);

    string WrapToWidth(TextMesh tm, string s, float maxW)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var para in s.Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' '))
            {
                string tryLine = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Width(tm, tryLine) > maxW) { sb.Append(line).Append('\n'); line = word; }
                else line = tryLine;
            }
            sb.Append(line).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    void FitSize(TextMesh tm, string text, float height, float maxW)
    {
        tm.characterSize = CharSize(height, tm);
        float w = Width(tm, text);
        if (w > maxW) tm.characterSize *= maxW / w;
    }

    void FitButton(TextMesh tm, string text, float maxW)
    {
        tm.characterSize = CharSize(buttonSize, tm);
        if (Width(tm, text) <= maxW) { tm.text = text; return; }
        tm.characterSize = CharSize(buttonSize * 0.88f, tm);
        string two = WrapToWidth(tm, text, maxW);
        while (two.Split('\n').Length > 2 && tm.characterSize > CharSize(buttonSize * 0.55f, tm))
        {
            tm.characterSize *= 0.9f;
            two = WrapToWidth(tm, text, maxW);
        }
        tm.text = two;
    }

    // ================================================================== desktop (DesktopMode.OnGUI)
    /// <summary>Draws the board on screen; returns its rectangle (mouse clicks there don't reach the cab).</summary>
    public Rect DrawGUI(float u, GUIStyle labelStyle, GUIStyle buttonStyle, GUIStyle boxStyle)
    {
        float gap = u * 0.2f;
        float tw = screenCentered ? Mathf.Min(Screen.width * (Picture != null ? 0.8f : 0.6f), u * (Picture != null ? 52f : 36f))
                                  : Mathf.Min(Screen.width * 0.42f, u * 30f);
        float inner = tw - u;
        var titleStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
        if (screenCentered) titleStyle.fontSize = Mathf.RoundToInt(labelStyle.fontSize * 1.5f);
        var btn = new GUIStyle(buttonStyle) { wordWrap = true };
        var starStyle = new GUIStyle(labelStyle) { normal = { textColor = new Color(1f, 0.8f, 0.2f) }, alignment = TextAnchor.MiddleRight };
        string StarText(int n) => new string('★', n) + new string('☆', 5 - n);
        float titleH = titleStyle.CalcHeight(new GUIContent(Title), inner);
        float starH = Stars >= 0 ? u * 1.2f : 0f;
        float bodyH = labelStyle.CalcHeight(new GUIContent(Body), inner);
        float picH = Picture != null ? inner * Picture.height / Picture.width + u * 0.3f : 0f;
        int nb = buttons.Count;
        string Label(int i) => buttons[i].stars >= 0 ? buttons[i].label + "   " + StarText(buttons[i].stars) : buttons[i].label;
        float widest = 0f;
        for (int i = 0; i < nb; i++) widest = Mathf.Max(widest, btn.CalcSize(new GUIContent(Label(i))).x + u * 0.4f);
        int cols = nb <= 1 ? 1 : nb <= 4 ? Mathf.Min(nb, maxColumns) : Mathf.Min(maxColumns, (nb + 1) / 2);
        while (cols > 1 && widest > (inner - (cols - 1) * gap) / cols) cols--;
        float bw = (inner - (cols - 1) * gap) / cols;
        int rows = (nb + cols - 1) / cols;
        var rowH = new float[Mathf.Max(1, rows)];
        for (int i = 0; i < nb; i++)
            rowH[i / cols] = Mathf.Max(rowH[i / cols], Mathf.Max(u * 1.1f, btn.CalcHeight(new GUIContent(Label(i)), bw) + u * 0.2f));
        float buttonsH = 0f;
        for (int r = 0; r < rows; r++) buttonsH += rowH[r] + gap;
        float h = u * 0.3f + titleH + starH + picH + u * 0.3f + bodyH + u * 0.4f + buttonsH + u * 0.3f;
        Rect box = screenCentered ? new Rect((Screen.width - tw) * 0.5f, Mathf.Max(u, (Screen.height - h) * 0.5f), tw, h)
                                  : new Rect(u * 0.5f, u, tw, h);
        GUI.Box(box, GUIContent.none, boxStyle);
        if (screenCentered) GUI.Box(box, GUIContent.none, boxStyle);   // darker behind the big menu
        float y = box.y + u * 0.3f;
        GUI.Label(new Rect(box.x + u * 0.5f, y, inner, titleH), Title, titleStyle);
        y += titleH;
        if (Stars >= 0)
        {
            var big = new GUIStyle(starStyle) { fontSize = Mathf.RoundToInt(labelStyle.fontSize * 1.6f), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(box.x + u * 0.5f, y, inner, starH), StarText(Stars), big);
            y += starH;
        }
        if (picH > 0f)
        {
            GUI.DrawTexture(new Rect(box.x + u * 0.5f, y + u * 0.15f, inner, picH - u * 0.3f), Picture, ScaleMode.ScaleToFit);
            y += picH;
        }
        y += u * 0.3f;
        GUI.Label(new Rect(box.x + u * 0.5f, y, inner, bodyH), Body, labelStyle);
        y += bodyH + u * 0.4f;
        int pressed = -1;
        for (int i = 0; i < nb; i++)
        {
            int r = i / cols, c = i % cols;
            float ry = y;
            for (int k = 0; k < r; k++) ry += rowH[k] + gap;
            if (GUI.Button(new Rect(box.x + u * 0.5f + c * (bw + gap), ry, bw, rowH[r]), Label(i), btn)) pressed = i;
        }
        if (pressed >= 0) Press(pressed);
        return box;
    }
}
