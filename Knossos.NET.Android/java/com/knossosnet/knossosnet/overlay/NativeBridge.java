package com.knossosnet.knossosnet.overlay;

public class NativeBridge {
    static { System.loadLibrary("touchcontrols"); }

    //COMMON
    public static final int CODE_ESC = 1;
    public static final int CODE_F3 = 2;
    public static final int CODE_ALT_M = 3;
    public static final int CODE_ALT_H = 4;
    public static final int CODE_ALT_J = 5;
    public static final int CODE_ALT_A = 6;

    //Weapons Area
    public static final int CODE_KEY_SPACE = 20;
    public static final int CODE_KEY_CTRL = 21;
    public static final int CODE_KEY_CYCLE_P = 22;
    public static final int CODE_KEY_CYCLE_S = 23;

    // General control
    public static final int CODE_KEY_TAB = 40;
    public static final int CODE_KEY_PLUS = 41;
    public static final int CODE_KEY_MINUS = 42;
    public static final int CODE_KEY_Q = 43;
    public static final int CODE_KEY_X = 44;
    public static final int CODE_KEY_M = 45;
    public static final int CODE_KEY_A = 46;
    public static final int CODE_KEY_Z = 47;
    public static final int CODE_KEY_BACKSLASH = 48;
    public static final int CODE_KEY_BACKSPACE = 49;

    //Targeting
    public static final int CODE_KEY_Y = 30;
    public static final int CODE_KEY_H = 31;
    public static final int CODE_KEY_B = 32;
    public static final int CODE_KEY_E = 33;
    public static final int CODE_KEY_F = 34;
    public static final int CODE_KEY_T = 35;
    public static final int CODE_KEY_S = 36;

    //Macros
    public static final int C_3_1 = 1;
    public static final int C_3_6 = 2;
    public static final int C_3_9 = 3;
    public static final int C_5 = 4;
    public static final int C_3_5 = 5;

    public static native void onButton(int code, boolean down);
    public static native void mouseStart();
    public static native void mouseStop();
    public static native void mouseTick(float nx, float ny);

    public static native void runMacro(int id);
    public static native void cancelMacros();
	public static native void setTextInputEnabled(boolean enabled);
}