// Xbox-style controllers (anything that works through Windows' XInput: Xbox controllers, and most
// PC gamepads). Turns buttons and sticks into the same actions as the keyboard and phone remote.

using System;
using System.Runtime.InteropServices;

namespace Theater
{
    class Gamepad
    {
        [StructLayout(LayoutKind.Sequential)]
        struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger, RightTrigger;
            public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Pad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int GetState14(int user, out XInputState state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int GetState91(int user, out XInputState state);

        const ushort DPadUp = 0x0001, DPadDown = 0x0002, DPadLeft = 0x0004, DPadRight = 0x0008, Start = 0x0010, BackBtn = 0x0020,
            LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        readonly Action<Act> send;

        bool useOld, unavailable;
        readonly bool[] connected = new bool[4];
        DateTime nextScan = DateTime.MinValue;
        ushort lastButtons;
        bool lastLT, lastRT;
        Act heldDir = Act.None;
        DateTime nextRepeat;

        public Gamepad(Action<Act> send) { this.send = send; }

        bool Read(int user, out XInputState s)
        {
            s = new XInputState();
            if (unavailable) return false;
            try { return (useOld ? GetState91(user, out s) : GetState14(user, out s)) == 0; }
            catch (DllNotFoundException)
            {
                if (!useOld) { useOld = true; return Read(user, out s); }
                unavailable = true;
                return false;
            }
            catch (EntryPointNotFoundException) { unavailable = true; return false; }
        }

        // Call ~30 times a second while the app is in front.
        public void Poll()
        {
            if (unavailable) return;
            XInputState s;
            // Asking an empty slot is slow, so look for newly connected controllers only every 2 seconds.
            if (DateTime.Now >= nextScan)
            {
                for (int i = 0; i < 4; i++) connected[i] = Read(i, out s);
                nextScan = DateTime.Now.AddSeconds(2);
            }
            ushort buttons = 0;
            byte lt = 0, rt = 0;
            short lx = 0, ly = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!connected[i]) continue;
                if (!Read(i, out s)) { connected[i] = false; continue; }
                buttons |= s.Pad.Buttons;
                lt = Math.Max(lt, s.Pad.LeftTrigger);
                rt = Math.Max(rt, s.Pad.RightTrigger);
                if (Math.Abs((int)s.Pad.ThumbLX) > Math.Abs((int)lx)) lx = s.Pad.ThumbLX;
                if (Math.Abs((int)s.Pad.ThumbLY) > Math.Abs((int)ly)) ly = s.Pad.ThumbLY;
            }


            // Buttons fire once when pressed.
            ushort pressed = (ushort)(buttons & ~lastButtons);
            lastButtons = buttons;
            if ((pressed & A) != 0) send(Act.Ok);
            if ((pressed & B) != 0) send(Act.Back);
            if ((pressed & X) != 0) send(Act.PlayPause);
            if ((pressed & Y) != 0) send(Act.Info);
            if ((pressed & Start) != 0) send(Act.Menu);
            if ((pressed & BackBtn) != 0) send(Act.Home);
            if ((pressed & LB) != 0) send(Act.Rewind);
            if ((pressed & RB) != 0) send(Act.Forward);

            // Triggers: volume.
            bool ltDown = lt > 100, rtDown = rt > 100;
            if (ltDown && !lastLT) send(Act.VolDown);
            if (rtDown && !lastRT) send(Act.VolUp);
            lastLT = ltDown;
            lastRT = rtDown;

            // D-pad or left stick: move, repeating while held.
            const int dead = 16000;
            Act dir = Act.None;
            if ((buttons & DPadUp) != 0 || ly > dead) dir = Act.Up;
            else if ((buttons & DPadDown) != 0 || ly < -dead) dir = Act.Down;
            else if ((buttons & DPadLeft) != 0 || lx < -dead) dir = Act.Left;
            else if ((buttons & DPadRight) != 0 || lx > dead) dir = Act.Right;
            if (dir == Act.None) { heldDir = Act.None; return; }
            if (dir != heldDir)
            {
                heldDir = dir;
                send(dir);
                nextRepeat = DateTime.Now.AddMilliseconds(400);
            }
            else if (DateTime.Now >= nextRepeat)
            {
                send(dir);
                nextRepeat = DateTime.Now.AddMilliseconds(130);
            }
        }
    }
}
