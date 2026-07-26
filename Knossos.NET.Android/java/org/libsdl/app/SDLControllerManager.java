package org.libsdl.app;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

import android.content.Context;
import android.hardware.lights.Light;
import android.hardware.lights.LightsRequest;
import android.hardware.lights.LightsManager;
import android.hardware.lights.LightState;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.graphics.Color;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;
import android.util.Log;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;


public class SDLControllerManager
{

    static native void nativeSetupJNI();

    static native void nativeAddJoystick(int device_id, String name, String desc,
                                                int vendor_id, int product_id,
                                                int button_mask,
                                                int naxes, int axis_mask, int nhats, boolean can_rumble, boolean has_rgb_led,
                                                boolean has_accelerometer, boolean has_gyroscope);
    static native void nativeRemoveJoystick(int device_id);
    static native void nativeAddHaptic(int device_id, String name);
    static native void nativeRemoveHaptic(int device_id);
    static public native boolean onNativePadDown(int device_id, int keycode, int scancode);
    static public native boolean onNativePadUp(int device_id, int keycode, int scancode);
    static native void onNativeJoy(int device_id, int axis,
                                          float value);
    static native void onNativeHat(int device_id, int hat_id,
                                          int x, int y);
    static native void onNativeJoySensor(int device_id, int sensor_type, long sensor_timestamp, float x, float y, float z);

    protected static SDLJoystickHandler mJoystickHandler;
    protected static SDLHapticHandler mHapticHandler;

    private static final String TAG = "SDLControllerManager";

    static void initialize() {
        if (mJoystickHandler == null) {
            mJoystickHandler = new SDLJoystickHandler();
        }

        if (mHapticHandler == null) {
            if (Build.VERSION.SDK_INT >= 31 /* Android 12.0 (S) */) {
                mHapticHandler = new SDLHapticHandler_API31();
            } else if (Build.VERSION.SDK_INT >= 26 /* Android 8.0 (O) */) {
                mHapticHandler = new SDLHapticHandler_API26();
            } else {
                mHapticHandler = new SDLHapticHandler();
            }
        }
    }

    // Joystick glue code, just a series of stubs that redirect to the SDLJoystickHandler instance
    static public boolean handleJoystickMotionEvent(MotionEvent event) {
        return mJoystickHandler.handleMotionEvent(event);
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void pollInputDevices() {
        mJoystickHandler.pollInputDevices();
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void joystickSetLED(int device_id, int red, int green, int blue) {
        mJoystickHandler.setLED(device_id, red, green, blue);
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void joystickSetSensorsEnabled(int device_id, boolean enabled) {
        mJoystickHandler.setSensorsEnabled(device_id, enabled);
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void pollHapticDevices() {
        mHapticHandler.pollHapticDevices();
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void hapticRun(int device_id, float intensity, int length) {
        mHapticHandler.run(device_id, intensity, length);
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void hapticRumble(int device_id, float low_frequency_intensity, float high_frequency_intensity, int length) {
        mHapticHandler.rumble(device_id, low_frequency_intensity, high_frequency_intensity, length);
    }

    /**
     * This method is called by SDL using JNI.
     */
    static void hapticStop(int device_id)
    {
        mHapticHandler.stop(device_id);
    }

    // Check if a given device is considered a possible SDL joystick
    static public boolean isDeviceSDLJoystick(int deviceId) {
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