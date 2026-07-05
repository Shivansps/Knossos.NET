package org.libsdl.app;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.Log;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;


public class SDLControllerManager
{

    public static native int nativeSetupJNI();

    public static native int nativeAddJoystick(int device_id, String name, String desc,
                                               int vendor_id, int product_id,
                                               boolean is_accelerometer, int button_mask,
                                               int naxes, int axis_mask, int nhats, int nballs);
    public static native int nativeRemoveJoystick(int device_id);
    public static native int nativeAddHaptic(int device_id, String name);
    public static native int nativeRemoveHaptic(int device_id);
    public static native int onNativePadDown(int device_id, int keycode);
    public static native int onNativePadUp(int device_id, int keycode);
    public static native void onNativeJoy(int device_id, int axis,
                                          float value);
    public static native void onNativeHat(int device_id, int hat_id,
                                          int x, int y);

    protected static SDLJoystickHandler mJoystickHandler;
    protected static SDLHapticHandler mHapticHandler;

    private static final String TAG = "SDLControllerManager";

    public static void initialize() {
        if (mJoystickHandler == null) {
            if (Build.VERSION.SDK_INT >= 19 /* Android 4.4 (KITKAT) */) {
                mJoystickHandler = new SDLJoystickHandler_API19();
            } else {
                mJoystickHandler = new SDLJoystickHandler_API16();
            }
        }

        if (mHapticHandler == null) {
            if (Build.VERSION.SDK_INT >= 26 /* Android 8.0 (O) */) {
                mHapticHandler = new SDLHapticHandler_API26();
            } else {
                mHapticHandler = new SDLHapticHandler();
            }
        }
    }

    // Joystick glue code, just a series of stubs that redirect to the SDLJoystickHandler instance
    public static boolean handleJoystickMotionEvent(MotionEvent event) {
        return mJoystickHandler.handleMotionEvent(event);
    }

    /**
     * This method is called by SDL using JNI.
     */
    public static void pollInputDevices() {
        mJoystickHandler.pollInputDevices();
    }

    /**
     * This method is called by SDL using JNI.
     */
    public static void pollHapticDevices() {
        mHapticHandler.pollHapticDevices();
    }

    /**
     * This method is called by SDL using JNI.
     */
    public static void hapticRun(int device_id, float intensity, int length) {
        mHapticHandler.run(device_id, intensity, length);
    }

    /**
     * This method is called by SDL using JNI.
     */
    public static void hapticStop(int device_id)
    {
        mHapticHandler.stop(device_id);
    }

    // Check if a given device is considered a possible SDL joystick
    public static boolean isDeviceSDLJoystick(int deviceId) {
        InputDevice device = InputDevice.getDevice(deviceId);
        // We cannot use InputDevice.isVirtual before API 16, so let's accept
        // only nonnegative device ids (VIRTUAL_KEYBOARD equals -1)
        if ((device == null) || (deviceId < 0)) {
            return false;
        }
        int sources = device.getSources();

        /* This is called for every button press, so let's not spam the logs */
        /*
        if ((sources & InputDevice.SOURCE_CLASS_JOYSTICK) != 0) {
            Log.v(TAG, "Input device " + device.getName() + " has class joystick.");
        }
        if ((sources & InputDevice.SOURCE_DPAD) == InputDevice.SOURCE_DPAD) {
            Log.v(TAG, "Input device " + device.getName() + " is a dpad.");
        }
        if ((sources & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD) {
            Log.v(TAG, "Input device " + device.getName() + " is a gamepad.");
        }
        */

        return ((sources & InputDevice.SOURCE_CLASS_JOYSTICK) != 0 ||
                ((sources & InputDevice.SOURCE_DPAD) == InputDevice.SOURCE_DPAD) ||
                ((sources & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD)
        );
    }

}



class SDLGenericMotionListener_API24 extends SDLGenericMotionListener_API12 {
    // Generic Motion (mouse hover, joystick...) events go here

    private boolean mRelativeModeEnabled;

    @Override
    public boolean onGenericMotion(View v, MotionEvent event) {

        // Handle relative mouse mode
        if (mRelativeModeEnabled) {
            if (event.getSource() == InputDevice.SOURCE_MOUSE) {
                int action = event.getActionMasked();
                if (action == MotionEvent.ACTION_HOVER_MOVE) {
                    float x = event.getAxisValue(MotionEvent.AXIS_RELATIVE_X);
                    float y = event.getAxisValue(MotionEvent.AXIS_RELATIVE_Y);
                    SDLActivity.onNativeMouse(0, action, x, y, true);
                    return true;
                }
            }
        }

        // Event was not managed, call SDLGenericMotionListener_API12 method
        return super.onGenericMotion(v, event);
    }

    @Override
    public boolean supportsRelativeMouse() {
        return true;
    }

    @Override
    public boolean inRelativeMode() {
        return mRelativeModeEnabled;
    }

    @Override
    public boolean setRelativeMouseEnabled(boolean enabled) {
        mRelativeModeEnabled = enabled;
        return true;
    }

    @Override
    public float getEventX(MotionEvent event) {
        if (mRelativeModeEnabled) {
            return event.getAxisValue(MotionEvent.AXIS_RELATIVE_X);
        } else {
            return event.getX(0);
        }
    }

    @Override
    public float getEventY(MotionEvent event) {
        if (mRelativeModeEnabled) {
            return event.getAxisValue(MotionEvent.AXIS_RELATIVE_Y);
        } else {
            return event.getY(0);
        }
    }
}

class SDLGenericMotionListener_API26 extends SDLGenericMotionListener_API24 {
    // Generic Motion (mouse hover, joystick...) events go here
    private boolean mRelativeModeEnabled;

    @Override
    public boolean onGenericMotion(View v, MotionEvent event) {
        float x, y;
        int action;

        switch ( event.getSource() ) {
            case InputDevice.SOURCE_JOYSTICK:
                return SDLControllerManager.handleJoystickMotionEvent(event);

            case InputDevice.SOURCE_MOUSE:
            // DeX desktop mouse cursor is a separate non-standard input type.
            case InputDevice.SOURCE_MOUSE | InputDevice.SOURCE_TOUCHSCREEN:
                action = event.getActionMasked();
                switch (action) {
                    case MotionEvent.ACTION_SCROLL:
                        x = event.getAxisValue(MotionEvent.AXIS_HSCROLL, 0);
                        y = event.getAxisValue(MotionEvent.AXIS_VSCROLL, 0);
                        SDLActivity.onNativeMouse(0, action, x, y, false);
                        return true;

                    case MotionEvent.ACTION_HOVER_MOVE:
                        x = event.getX(0);
                        y = event.getY(0);
                        SDLActivity.onNativeMouse(0, action, x, y, false);
                        return true;

                    default:
                        break;
                }
                break;

            case InputDevice.SOURCE_MOUSE_RELATIVE:
                action = event.getActionMasked();
                switch (action) {
                    case MotionEvent.ACTION_SCROLL:
                        x = event.getAxisValue(MotionEvent.AXIS_HSCROLL, 0);
                        y = event.getAxisValue(MotionEvent.AXIS_VSCROLL, 0);
                        SDLActivity.onNativeMouse(0, action, x, y, false);
                        return true;

                    case MotionEvent.ACTION_HOVER_MOVE:
                        x = event.getX(0);
                        y = event.getY(0);
                        SDLActivity.onNativeMouse(0, action, x, y, true);
                        return true;

                    default:
                        break;
                }
                break;

            default:
                break;
        }

        // Event was not managed
        return false;
    }

    @Override
    public boolean supportsRelativeMouse() {
        return (!SDLActivity.isDeXMode() || Build.VERSION.SDK_INT >= 27 /* Android 8.1 (O_MR1) */);
    }

    @Override
    public boolean inRelativeMode() {
        return mRelativeModeEnabled;
    }

    @Override
    public boolean setRelativeMouseEnabled(boolean enabled) {
        if (!SDLActivity.isDeXMode() || Build.VERSION.SDK_INT >= 27 /* Android 8.1 (O_MR1) */) {
            if (enabled) {
                SDLActivity.getContentView().requestPointerCapture();
            } else {
                SDLActivity.getContentView().releasePointerCapture();
            }
            mRelativeModeEnabled = enabled;
            return true;
        } else {
            return false;
        }
    }

    @Override
    public void reclaimRelativeMouseModeIfNeeded()
    {
        if (mRelativeModeEnabled && !SDLActivity.isDeXMode()) {
            SDLActivity.getContentView().requestPointerCapture();
        }
    }

    @Override
    public float getEventX(MotionEvent event) {
        // Relative mouse in capture mode will only have relative for X/Y
        return event.getX(0);
    }

    @Override
    public float getEventY(MotionEvent event) {
        // Relative mouse in capture mode will only have relative for X/Y
        return event.getY(0);
    }
}
