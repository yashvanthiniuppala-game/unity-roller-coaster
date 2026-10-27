// RideHUD.cs  —  shows ride time, speed, height and G-force on screen.
// Put it on any object (e.g. the Main Camera). Press H to hide it (e.g. while recording).
using UnityEngine;

public class RideHUD : MonoBehaviour
{
    public CoasterCart cart;
    public string rideName = "Desert Serpent";
    public bool show = true;

    GUIStyle box;

    void Start() { if (cart == null) cart = FindAnyObjectByType<CoasterCart>(); }

    void Update() { if (CoasterInput.Pressed('h')) show = !show; }

    void OnGUI()
    {
        if (!show || cart == null) return;
        if (box == null)
        {
            box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, richText = true };
            box.normal.textColor = Color.white;
        }

        string status = cart.state == CoasterCart.RideState.WaitingInStation ? "<color=#8cf>In station</color>"
                      : cart.onLift ? "<color=#ff4>Chain lift</color>"
                      : cart.braking ? "<color=#f66>Braking</color>" : "<color=#6f6>Riding</color>";
        string text =
            $"<b>{rideName}</b>\n" +
            $"Status: {status}\n" +
            $"Ride time: {cart.rideTime:F1} s\n" +
            $"Last ride: {(cart.lastRideTime > 0 ? cart.lastRideTime.ToString("F1") + " s" : "-")}\n" +
            $"Speed: {cart.speed * 3.6f:F0} km/h\n" +
            $"Height: {cart.Height:F1} m\n" +
            $"G-force: {cart.gForce:F1} g";
        GUI.Box(new Rect(12, 12, 230, 170), text, box);
        GUI.Label(new Rect(12, Screen.height - 34, 620, 26), "C / 1-4: camera    Space: start ride    H: hide HUD", box);
    }
}
