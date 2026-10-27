// CoasterInput.cs — reads keys with BOTH the new Input System and the old Input Manager,
// so the kit works whatever "Active Input Handling" your project uses.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class CoasterInput
{
    public static bool Pressed(char key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            switch (char.ToLower(key))
            {
                case 'c': return kb.cKey.wasPressedThisFrame;
                case 'h': return kb.hKey.wasPressedThisFrame;
                case 'r': return kb.rKey.wasPressedThisFrame;
                case ' ': return kb.spaceKey.wasPressedThisFrame;
                case '1': return kb.digit1Key.wasPressedThisFrame;
                case '2': return kb.digit2Key.wasPressedThisFrame;
                case '3': return kb.digit3Key.wasPressedThisFrame;
                case '4': return kb.digit4Key.wasPressedThisFrame;
            }
        }
        return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
        switch (char.ToLower(key))
        {
            case 'c': return Input.GetKeyDown(KeyCode.C);
            case 'h': return Input.GetKeyDown(KeyCode.H);
            case 'r': return Input.GetKeyDown(KeyCode.R);
            case ' ': return Input.GetKeyDown(KeyCode.Space);
            case '1': return Input.GetKeyDown(KeyCode.Alpha1);
            case '2': return Input.GetKeyDown(KeyCode.Alpha2);
            case '3': return Input.GetKeyDown(KeyCode.Alpha3);
            case '4': return Input.GetKeyDown(KeyCode.Alpha4);
        }
        return false;
#else
        return false;
#endif
    }
}
